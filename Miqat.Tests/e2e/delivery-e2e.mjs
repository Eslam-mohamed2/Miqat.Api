// Checks that notifications really leave the API: as email, and as an
// encrypted Web Push message a browser could read. Nothing here is mocked
// inside the API - it sends to a mail server and a push service, which are
// the stand-ins of delivery-fakes.mjs, and this script reads back what
// arrived and (for push) decrypts it the way a browser does.
//
// Needs an API started with a mail server, push keys and notification emails
// switched on, all pointing at the stand-ins:
//
//   EmailSettings__SmtpHost=miqat-fakes         EmailSettings__SmtpPort=2525
//   EmailSettings__FromEmail=noreply@miqat.test Notifications__EmailEnabled=true
//   Push__PublicKey=...  Push__PrivateKey=...   (a VAPID key pair)
//   Push__AllowedHosts__0=miqat-fakes           Push__AllowInsecureEndpoints=true
//   WeeklySummary__Day=<today>  WeeklySummary__Hour=0  WeeklySummary__IntervalSeconds=10
//   Seed__DemoData=true
//
// Settings: BASE (the API, default http://localhost:5002), FAKES (captured
// data, default http://localhost:2527), PUSH_HOST (how the API reaches the
// push stand-in, default http://miqat-fakes:2526).
import crypto from 'node:crypto';

const BASE = process.env.BASE || 'http://localhost:5002';
const FAKES = process.env.FAKES || 'http://localhost:2527';
const PUSH_HOST = process.env.PUSH_HOST || 'http://miqat-fakes:2526';
const run = Date.now().toString(36);
const U2 = '00000002-0000-0000-0000-000000000000';

let pass = 0, fail = 0;
const failures = [];
function check(name, ok, detail = '') {
  ok ? pass++ : (fail++, failures.push(`${name}  -> ${detail}`));
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : '  -> ' + detail}`);
}
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function call(method, path, { token, body } = {}) {
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not json */ }
  return { status: res.status, json, text };
}
const login = async email => (await call('POST', '/api/Auth/login', { body: { email, password: 'Test@1234' } })).json;
const captured = async what => (await fetch(`${FAKES}/${what}`)).json();
async function waitFor(what, match, seconds = 20) {
  for (let i = 0; i < seconds * 2; i++) {
    const found = (await captured(what)).find(match);
    if (found) return found;
    await sleep(500);
  }
  return null;
}

// ── Reading mail ────────────────────────────────────────────────────────────
const headerOf = (mail, name) => (mail.data.match(new RegExp(`^${name}:\\s*(.*(?:\\r\\n[ \\t].*)*)`, 'mi')) || [])[1]?.replace(/\r\n[ \t]/g, ' ') || '';
/** The text of a message whatever way the mail library encoded its parts. */
function readable(mail) {
  const unQuoted = mail.data.replace(/=\r\n/g, '').replace(/=([0-9A-F]{2})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)));
  const fromBase64 = (mail.data.match(/(?:^[A-Za-z0-9+/=]{40,}\r?\n?)+/gm) || [])
    .map(block => { try { return Buffer.from(block.replace(/\s/g, ''), 'base64').toString('utf8'); } catch { return ''; } });
  return [mail.data, Buffer.from(unQuoted, 'latin1').toString('utf8'), ...fromBase64].join('\n');
}

// ── Reading a push message the way a browser does (RFC 8291) ────────────────
const b64url = buffer => Buffer.from(buffer).toString('base64url');
function newBrowser() {
  const keys = crypto.createECDH('prime256v1');
  keys.generateKeys();
  return { keys, publicKey: keys.getPublicKey(), auth: crypto.randomBytes(16) };
}
function decryptPush(browser, bodyBase64) {
  const body = Buffer.from(bodyBase64, 'base64');
  const salt = body.subarray(0, 16);
  const recordSize = body.readUInt32BE(16);
  const keyLength = body.readUInt8(20);
  const senderKey = body.subarray(21, 21 + keyLength);
  const encrypted = body.subarray(21 + keyLength);

  const hkdf = (ikm, saltBytes, info, length) => Buffer.from(crypto.hkdfSync('sha256', ikm, saltBytes, info, length));
  const shared = browser.keys.computeSecret(senderKey);
  const inputKey = hkdf(shared, browser.auth, Buffer.concat([Buffer.from('WebPush: info\0'), browser.publicKey, senderKey]), 32);
  const key = hkdf(inputKey, salt, Buffer.from('Content-Encoding: aes128gcm\0'), 16);
  const nonce = hkdf(inputKey, salt, Buffer.from('Content-Encoding: nonce\0'), 12);

  const decipher = crypto.createDecipheriv('aes-128-gcm', key, nonce);
  decipher.setAuthTag(encrypted.subarray(encrypted.length - 16));
  const padded = Buffer.concat([decipher.update(encrypted.subarray(0, encrypted.length - 16)), decipher.final()]);
  const end = padded.lastIndexOf(2); // the payload ends with 0x02
  return { recordSize, lastByte: padded[padded.length - 1], json: JSON.parse(padded.subarray(0, end).toString('utf8')) };
}
/** Checks the "this server sent it" token against the server's public key. */
function readToken(authorization, serverPublicKey) {
  const [, token, key] = authorization.match(/^vapid t=([^,]+), k=(.+)$/) || [];
  if (!token) return null;
  const [header, claims, signature] = token.split('.');
  const point = Buffer.from(serverPublicKey, 'base64url');
  const publicKey = crypto.createPublicKey({ format: 'jwk', key: { kty: 'EC', crv: 'P-256', x: b64url(point.subarray(1, 33)), y: b64url(point.subarray(33)) } });
  return {
    key,
    header: JSON.parse(Buffer.from(header, 'base64url')),
    claims: JSON.parse(Buffer.from(claims, 'base64url')),
    signed: crypto.verify('sha256', Buffer.from(`${header}.${claims}`), { key: publicKey, dsaEncoding: 'ieee-p1363' }, Buffer.from(signature, 'base64url')),
  };
}

// ────────────────────────────────────────────────────────────────────────────
const admin = await login('eslam@miqat.app');
const sarah = await login('sarah@miqat.app');
const A = admin.accessToken, S = sarah.accessToken;
const assign = async title => (await call('POST', '/api/Task', { token: A, body: { title, priority: 'High', assignedToUserId: U2 } })).json?.id;

console.log('\n== Web Push ==');
let r = await call('GET', '/api/Device/push-key', { token: S });
const serverKey = r.json?.publicKey;
check('the server has push keys and hands out the public one', r.json?.isConfigured === true && Buffer.from(serverKey || '', 'base64url').length === 65, JSON.stringify(r.json));

const browser = newBrowser();
const pushPath = `/push/sarah-${run}`;
r = await call('POST', '/api/Device', { token: S, body: { endpoint: PUSH_HOST + pushPath, p256dh: b64url(browser.publicKey), auth: b64url(browser.auth), name: 'Test browser' } });
check('register the browser -> 200', r.status === 200, `${r.status} ${r.text.slice(0, 160)}`);

const firstTitle = `Review <b>budget</b> ${run}`;
const firstTask = await assign(firstTitle);
const push = await waitFor('pushes', p => p.url === pushPath);
check('assigning her a task sends a push to her browser', !!push, 'nothing arrived at the push service');
if (push) {
  check('...in the standard encoding, kept for a day if the device is offline', push.headers['content-encoding'] === 'aes128gcm' && push.headers.ttl === '86400' && push.method === 'POST', JSON.stringify(push.headers).slice(0, 200));
  const token = readToken(push.headers.authorization || '', serverKey);
  check('...signed by this server for this push service only (VAPID)', !!token && token.signed && token.key === serverKey && token.header.alg === 'ES256' && token.claims.aud === PUSH_HOST && /^mailto:|^https:/.test(token.claims.sub) && token.claims.exp > Date.now() / 1000 && token.claims.exp <= Date.now() / 1000 + 24 * 3600, JSON.stringify(token).slice(0, 260));
  let message = null;
  try { message = decryptPush(browser, push.body); } catch (e) { message = { error: String(e) }; }
  check('...and the browser can decrypt it: it is the notification', message?.json?.type === 'TaskAssigned' && message.json.title === 'Task assigned to you' && message.json.linkedEntityId === firstTask && message.json.message.includes(firstTitle) && message.lastByte === 2 && message.recordSize === 4096, JSON.stringify(message).slice(0, 260));
  const stranger = newBrowser();
  let leaked = true;
  try { decryptPush({ ...stranger, auth: browser.auth }, push.body); } catch { leaked = false; }
  check('...and nobody else can', leaked === false, 'another key pair decrypted it');
}

console.log('\n== Email ==');
const sarahEmail = sarah.email.toLowerCase();
let mail = await waitFor('mails', m => m.to.includes(sarahEmail) && readable(m).includes(run) && /Task assigned/.test(headerOf(m, 'Subject')));
check('the same notification arrives as an email', !!mail, 'no mail arrived');
if (mail) {
  const text = readable(mail);
  check('...from the configured sender, with a clear subject', /noreply@miqat\.test/.test(headerOf(mail, 'From')) && headerOf(mail, 'Subject') === 'Task assigned to you - Miqat', `${headerOf(mail, 'From')} | ${headerOf(mail, 'Subject')}`);
  check('...with a plain-text part next to the HTML part', /multipart\/alternative/i.test(mail.data) && /text\/plain/i.test(mail.data) && /text\/html/i.test(mail.data), mail.data.slice(0, 200));
  check('...and text people typed cannot inject HTML into it', text.includes('Review &lt;b&gt;budget&lt;/b&gt;') && !/<b>budget<\/b>[^]*<\/html>/i.test(text.split('text/html')[1] || ''), text.slice(text.indexOf('Review'), text.indexOf('Review') + 80));
}

r = await call('PUT', '/api/User/me/preferences', { token: S, body: { emailNotifications: false } });
const quietTitle = `No mail please ${run}`;
await assign(quietTitle);
const stillPushed = await waitFor('pushes', p => p.url === pushPath && (() => { try { return decryptPush(browser, p.body).json.message.includes(quietTitle); } catch { return false; } })());
await sleep(3000);
mail = (await captured('mails')).find(m => readable(m).includes(quietTitle));
check('with email notifications off: no email, the push still comes', !mail && !!stillPushed, `mail=${!!mail} push=${!!stillPushed}`);

r = await call('PUT', '/api/User/me/preferences', { token: S, body: { emailNotifications: true, pushNotifications: false } });
const silentTitle = `No push please ${run}`;
await assign(silentTitle);
mail = await waitFor('mails', m => readable(m).includes(silentTitle));
const pushedAnyway = (await captured('pushes')).some(p => { try { return p.url === pushPath && decryptPush(browser, p.body).json.message.includes(silentTitle); } catch { return false; } });
check('with push off: no push to the device, the email still comes', !!mail && !pushedAnyway, `mail=${!!mail} push=${pushedAnyway}`);
await call('PUT', '/api/User/me/preferences', { token: S, body: { pushNotifications: true } });

console.log('\n== A browser that unsubscribed ==');
const goneBrowser = newBrowser();
r = await call('POST', '/api/Device', { token: S, body: { endpoint: `${PUSH_HOST}/gone/sarah-${run}`, p256dh: b64url(goneBrowser.publicKey), auth: b64url(goneBrowser.auth), name: 'Uninstalled browser' } });
const goneId = r.json?.id;
await assign(`Clean up ${run}`);
let devices = [];
for (let i = 0; i < 20; i++) {
  devices = (await call('GET', '/api/Device', { token: S })).json || [];
  if (!devices.some(d => d.id === goneId)) break;
  await sleep(500);
}
check('a device the push service reports as gone is forgotten, the working one is kept', !devices.some(d => d.id === goneId) && devices.some(d => d.name === 'Test browser'), JSON.stringify(devices.map(d => d.name)));

console.log('\n== Sign-in codes by email ==');
const newEmail = `mail.${run}@example.com`;
await call('POST', '/api/Auth/register', { body: { fullName: 'Mail Test', email: newEmail, password: 'Passw0rd!x', timeZone: 'UTC' } });
mail = await waitFor('mails', m => m.to.includes(newEmail));
const code = (headerOf(mail || { data: '' }, 'Subject').match(/\b(\d{6})\b/) || [])[1];
check('a new account gets its code by email', !!mail && !!code, headerOf(mail || { data: '' }, 'Subject'));
r = await call('POST', '/api/Auth/verify-otp', { body: { email: newEmail, code, purpose: 'EmailVerification' } });
check('...and the code from the email verifies the account', r.status === 200 && !!r.json?.accessToken, `${r.status}`);

console.log('\n== Weekly summary ==');
const weeklyBefore = (await captured('mails')).filter(m => m.to.includes(sarahEmail) && /Your week on Miqat/.test(headerOf(m, 'Subject'))).length;
check('nobody gets the weekly summary without asking for it', weeklyBefore === 0, `count=${weeklyBefore}`);
r = await call('PUT', '/api/User/me/preferences', { token: S, body: { weeklySummary: true } });
mail = await waitFor('mails', m => m.to.includes(sarahEmail) && /Your week on Miqat/.test(headerOf(m, 'Subject')), 40);
check('switching it on sends the summary at the next weekly moment', !!mail, 'no summary mail arrived');
if (mail) {
  const text = readable(mail);
  check('...with the counts and what is coming up', ['Finished last week', 'Due this week', 'Overdue', 'Events this week', 'Coming up'].every(label => text.includes(label)), text.slice(0, 200));
}
await sleep(25000); // two more passes of the weekly job
const weeklyAfter = (await captured('mails')).filter(m => m.to.includes(sarahEmail) && /Your week on Miqat/.test(headerOf(m, 'Subject'))).length;
check('...once, not on every pass', weeklyAfter === 1, `count=${weeklyAfter}`);
await call('PUT', '/api/User/me/preferences', { token: S, body: { weeklySummary: false } });

console.log(`\n${pass} passed, ${fail} failed, ${pass + fail} total`);
if (fail) { console.log('\nFailures:'); failures.forEach(f => console.log(' - ' + f)); }
process.exit(fail ? 1 : 0);
