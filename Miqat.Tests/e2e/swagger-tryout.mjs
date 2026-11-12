// Presses "Execute" on every endpoint of the Swagger page, using nothing but
// the example data the Swagger document itself offers. It is the same thing a
// person does by hand in the browser: sign in with the prefilled login, then
// open each endpoint and run it without typing anything.
//
//   node swagger-tryout.mjs            safe: sign in + every GET endpoint
//   node swagger-tryout.mjs --all      every endpoint, including the ones that
//                                      create, change and DELETE data
//
// Settings (environment variables):
//   BASE       API address                      default http://localhost:5001
//   CONTAINER  Docker container of that API     default miqat-api-next
//              (only used to read one-time codes from its log, as a developer
//              does when no email provider is configured)
//
// Run --all against a database you can throw away: it deletes a task, a
// project and an account of the demo data.
import { execSync } from 'node:child_process';

const BASE = process.env.BASE || 'http://localhost:5001';
const CONTAINER = process.env.CONTAINER || 'miqat-api-next';
const ALL = process.argv.includes('--all');

const REFRESH_PLACEHOLDER = 'filled-in-automatically-after-sign-in';
const CODE_PLACEHOLDER = '123456';
const TINY_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64');

// What "choose a file" means for each upload endpoint: a calendar file for the
// import, a small picture everywhere else.
const TINY_ICS = [
  'BEGIN:VCALENDAR', 'VERSION:2.0', 'BEGIN:VEVENT', 'UID:swagger-tryout@example.com',
  'SUMMARY:Imported by the Swagger try-out', 'DTSTART:20270115T090000Z', 'DTEND:20270115T100000Z',
  'END:VEVENT', 'END:VCALENDAR', '',
].join('\r\n');

const fileFor = path => path.endsWith('/Calendar/import')
  ? { content: TINY_ICS, type: 'text/calendar', name: 'calendar.ics' }
  : { content: TINY_PNG, type: 'image/png', name: 'picture.png' };

let accessToken = null;
let refreshToken = null;
let doc = null;
const results = [];

async function loadDoc() {
  const res = await fetch(BASE + '/swagger/v1/swagger.json');
  if (!res.ok) throw new Error(`Swagger document not reachable at ${BASE} (HTTP ${res.status})`);
  doc = await res.json();
}

/** Turns one endpoint into a request, from the examples in the document only. */
function prepare(method, path, exampleName) {
  const op = doc.paths[path]?.[method];
  if (!op) return null;

  const missing = [];
  let url = path;
  const query = new URLSearchParams();

  for (const p of op.parameters || []) {
    const value = p.example ?? p.schema?.default;
    if (p.in === 'path') {
      if (p['x-sample-missing']) missing.push(p.name);
      url = url.replace(`{${p.name}}`, encodeURIComponent(value));
    } else if (p.in === 'query' && value !== undefined && value !== null) {
      query.set(p.name, value);
    }
  }

  const content = op.requestBody?.content || {};
  let body;
  let upload = false;
  if (content['application/json']) {
    const media = content['application/json'];
    body = media.example
      ?? (media.examples && (media.examples[exampleName] || Object.values(media.examples)[0])?.value);
  } else if (content['multipart/form-data']) {
    upload = true;
  }

  const qs = query.toString();
  return { url: url + (qs ? '?' + qs : ''), body, upload: upload ? fileFor(path) : null, missing, hasBody: !!op.requestBody };
}

async function send(method, request) {
  const headers = {};
  if (accessToken) headers.Authorization = 'Bearer ' + accessToken;

  let payload;
  if (request.upload) {
    payload = new FormData();
    payload.append('file', new Blob([request.upload.content], { type: request.upload.type }), request.upload.name);
  } else if (request.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    // What the script on the Swagger page does before a request leaves.
    payload = JSON.stringify(request.body).split(REFRESH_PLACEHOLDER).join(refreshToken || REFRESH_PLACEHOLDER);
  }

  const res = await fetch(BASE + request.url, { method: method.toUpperCase(), headers, body: payload });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not JSON */ }

  // What the script on the Swagger page does after an answer arrives.
  if (res.ok && json?.accessToken && request.url.includes('/api/Auth/')) {
    accessToken = json.accessToken;
    if (json.refreshToken) refreshToken = json.refreshToken;
  }

  return { status: res.status, json, text };
}

/** The newest one-time code the API logged for this email, or null. */
function codeFromLog(email, purpose) {
  try {
    const log = execSync(`docker logs --since 10m ${CONTAINER} 2>&1`, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    const lines = log.split('\n').filter(l =>
      l.includes('[Dev OTP]') && l.toLowerCase().includes(String(email).toLowerCase()) && l.includes(purpose));
    const match = lines.length && lines[lines.length - 1].match(/Code:\s*(\d{6})/);
    return match ? match[1] : null;
  } catch {
    return null;
  }
}

/**
 * Runs one endpoint and records what happened.
 *   fresh       reload the document first, like reloading the page, so the ids
 *               in the examples reflect what earlier calls created or deleted
 *   example     which named example to use when an endpoint offers several
 *   code        { purpose }: swap the placeholder for the code from the log
 *   expect      a status that is the correct answer although it is not 2xx
 */
async function run(method, path, { fresh = false, example, code, expect, why, label } = {}) {
  if (fresh) await loadDoc();

  const request = prepare(method, path, example);
  const name = `${method.toUpperCase().padEnd(6)} ${path}${label ? '  [' + label + ']' : ''}`;
  if (!request) return;

  if (request.missing.length) {
    results.push({ name, status: '-', verdict: 'SKIPPED', note: `no row to use for ${request.missing.join(', ')} in the demo data` });
    return null;
  }

  if (code && request.body) {
    const found = codeFromLog(request.body.email, code.purpose);
    if (!found) {
      results.push({ name, status: '-', verdict: 'NEEDS CODE', note: 'needs the 6-digit code from the email / API log' });
      return null;
    }
    request.body = JSON.parse(JSON.stringify(request.body).split(CODE_PLACEHOLDER).join(found));
  }

  const res = await send(method, request);
  const ok = res.status >= 200 && res.status < 300;

  if (ok) results.push({ name, status: res.status, verdict: 'OK', note: '' });
  else if (expect === res.status) results.push({ name, status: res.status, verdict: 'EXPECTED', note: why });
  else results.push({ name, status: res.status, verdict: 'FAILED', note: res.text.slice(0, 160).replace(/\s+/g, ' ') });

  return res;
}

const done = new Set();
const mark = (method, path) => done.add(method + ' ' + path);
const operations = () => Object.entries(doc.paths).flatMap(([path, item]) => Object.keys(item).map(method => ({ method, path })));

// ── 1. Sign in with the prefilled demo account ──────────────────────────────
await loadDoc();
const total = operations().length;
console.log(`Swagger document at ${BASE}: ${total} endpoints. Mode: ${ALL ? 'ALL (changes and deletes data)' : 'safe (GET only)'}\n`);

const signIn = await run('post', '/api/Auth/login');
mark('post', '/api/Auth/login');
if (!accessToken) {
  console.log(`Sign-in with the example in the document failed (HTTP ${signIn?.status}). ${signIn?.text?.slice(0, 200) || ''}`);
  process.exit(1);
}

if (ALL) {
  // ── 2. Account flows, on one "page load" so the example email stays the same
  await run('post', '/api/Auth/register');
  await run('post', '/api/Auth/resend-otp');
  await run('post', '/api/Auth/verify-otp', { example: 'New account', code: { purpose: 'EmailVerification' }, label: 'new account' });
  // verify-otp signed the page in as the new account; go back to the demo account.
  await run('post', '/api/Auth/login', { label: 'again' });

  await run('post', '/api/Auth/forgot-password');
  await run('post', '/api/Auth/verify-otp', { example: 'Password reset', code: { purpose: 'PasswordReset' }, label: 'password reset' });
  await run('post', '/api/Auth/reset-password', { code: { purpose: 'PasswordReset' } });
  // A reset signs every device out, so the saved refresh token is no longer valid.
  await run('post', '/api/Auth/login', { label: 'after reset' });

  await run('post', '/api/Auth/change-password');

  // Asking for the change is safe; confirming it would change the address the
  // demo account signs in with, so the example code is sent as it is and refused.
  await run('post', '/api/Auth/change-email');
  await run('post', '/api/Auth/change-email/confirm',
    { expect: 400, why: 'left unconfirmed on purpose: it would change the sign-in address of the demo account' });

  await run('post', '/api/Auth/google', { expect: 401, why: 'needs a real Google token; cannot be faked' });
  await run('post', '/api/Auth/refresh');
  for (const p of ['register', 'resend-otp', 'verify-otp', 'forgot-password', 'reset-password', 'change-password',
    'change-email', 'change-email/confirm', 'google', 'refresh'])
    mark('post', '/api/Auth/' + p);

  // ── 3. Everything that creates or changes, in document order ──────────────
  const skippedOnce = [];
  for (const { method, path } of operations()) {
    if (method === 'get' || method === 'delete' || done.has(method + ' ' + path) || path === '/api/Auth/logout') continue;
    const before = results.length;
    await run(method, path, { fresh: true });
    if (results[before]?.verdict === 'SKIPPED') skippedOnce.push({ method, path, index: before });
    else mark(method, path);
  }
  // Second chance: a row that was missing may have been created further down.
  for (const { method, path, index } of skippedOnce) {
    const before = results.length;
    await run(method, path, { fresh: true });
    if (results[before] && results[before].verdict !== 'SKIPPED') results[index] = null;
    else results.pop();
    mark(method, path);
  }
}

// ── 4. Every GET ────────────────────────────────────────────────────────────
for (const { method, path } of operations()) {
  if (method !== 'get') continue;
  await run(method, path, { fresh: ALL });
  mark(method, path);
}

if (ALL) {
  // ── 5. Deletes, children before parents ───────────────────────────────────
  const order = [
    '/api/Task/comments/{commentId}', '/api/Notification/{id}', '/api/Friends/{friendshipId}', '/api/Board/{id}',
    '/api/Reminder/{id}', '/api/Attachment/{id}', '/api/Device/{id}',
    '/api/Task/subtasks/{subtaskId}', '/api/Task/{taskId}/dependencies/{dependsOnTaskId}',
    '/api/Event/{id}/attendees/{userId}', '/api/Event/{id}/occurrences', '/api/Event/{id}', '/api/Calendar/{id}',
    '/api/Group/{groupId}/members/{userId}', '/api/Task/{id}', '/api/Group/{id}', '/api/User/profile-image', '/api/User/{id}',
  ];
  const deletes = operations().filter(o => o.method === 'delete')
    .sort((a, b) => order.indexOf(a.path) - order.indexOf(b.path));
  for (const { method, path } of deletes) {
    await run(method, path, { fresh: true });
    mark(method, path);
  }

  // ── 6. Sign out last ──────────────────────────────────────────────────────
  await run('post', '/api/Auth/logout');
  mark('post', '/api/Auth/logout');
}

// ── Report ──────────────────────────────────────────────────────────────────
const rows = results.filter(Boolean);
for (const r of rows) console.log(`${r.verdict.padEnd(10)} ${String(r.status).padEnd(4)} ${r.name}${r.note ? '   <- ' + r.note : ''}`);

const count = verdict => rows.filter(r => r.verdict === verdict).length;
const notTried = operations().filter(o => !done.has(o.method + ' ' + o.path));

console.log(`\n${done.size} of ${total} endpoints tried (${rows.length} calls).`);
console.log(`OK: ${count('OK')}   expected non-2xx: ${count('EXPECTED')}   skipped (no row to use): ${count('SKIPPED')}   needs a code: ${count('NEEDS CODE')}   FAILED: ${count('FAILED')}`);
if (!ALL) console.log(`${notTried.length} endpoints change data and were left alone. Run with --all on a throw-away database to try them too.`);

process.exit(count('FAILED') > 0 ? 1 : 0);
