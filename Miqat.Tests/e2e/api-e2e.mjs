// End-to-end check of the merged Miqat API (feat/stable-integration).
// Run: node apitest2.mjs        (BASE / CONTAINER can be overridden by env)
// Each check states the EXPECTED behaviour; a failure is a bug.
import { execSync } from 'node:child_process';

const BASE = process.env.BASE || 'http://localhost:5001';
const CONTAINER = process.env.CONTAINER || 'miqat-api-next';
const results = [];
let pass = 0, fail = 0;

async function call(method, path, { token, body, headers, form } = {}) {
  const h = { ...(headers || {}) };
  if (token) h.Authorization = `Bearer ${token}`;
  let payload;
  if (form) payload = form;
  else if (body !== undefined) { h['Content-Type'] = 'application/json'; payload = typeof body === 'string' ? body : JSON.stringify(body); }
  const res = await fetch(BASE + path, { method, headers: h, body: payload });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not json */ }
  return { status: res.status, json, text };
}
function check(name, ok, detail = '') {
  results.push({ name, ok: !!ok, detail });
  ok ? pass++ : fail++;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : '  -> ' + detail}`);
}
const brief = r => `${r.status} ${String(r.text).slice(0, 170)}`;
const sleep = ms => new Promise(r => setTimeout(r, ms));
const denied = r => r.status === 403 || r.status === 404;

async function login(email, password = 'Test@1234') {
  const r = await call('POST', '/api/Auth/login', { body: { email, password } });
  if (r.status !== 200) throw new Error(`login ${email} failed: ${brief(r)}`);
  return r.json;
}
function devOtpLines(email, purpose) {
  const logs = execSync(`docker logs --since 10m ${CONTAINER} 2>&1`, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return logs.split('\n').filter(l => l.includes('[Dev OTP]') && l.toLowerCase().includes(email.toLowerCase()) && (!purpose || l.includes(purpose)));
}
function otpFromLogs(email, purpose) {
  const lines = devOtpLines(email, purpose);
  const m = lines.length && lines[lines.length - 1].match(/Code:\s*(\d{6})/);
  return m ? m[1] : null;
}
const notifs = async token => (await call('GET', '/api/Notification', { token })).json || [];

/** Raw SignalR connection (JSON protocol over WebSocket) that records pushed events. */
function connectHub(token) {
  return new Promise((resolve, reject) => {
    const url = `${BASE.replace(/^http/, 'ws')}/hubs/miqat${token ? `?access_token=${encodeURIComponent(token)}` : ''}`;
    const ws = new WebSocket(url);
    const events = [];
    const completions = new Map(); // invocationId -> result of a hub method that was called
    let ready = false;
    const timer = setTimeout(() => { if (!ready) { try { ws.close(); } catch {} reject(new Error('hub timeout')); } }, 8000);
    ws.onopen = () => ws.send('{"protocol":"json","version":1}\x1e');
    ws.onmessage = ev => {
      for (const part of String(ev.data).split('\x1e').filter(Boolean)) {
        let msg; try { msg = JSON.parse(part); } catch { continue; }
        if (!ready && msg.type === undefined && !msg.error) { ready = true; clearTimeout(timer); resolve({ ws, events, completions }); continue; }
        if (msg.type === 1) events.push({ target: msg.target, args: msg.arguments || [] });
        if (msg.type === 3) completions.set(msg.invocationId, msg);
      }
    };
    ws.onerror = () => { if (!ready) { clearTimeout(timer); reject(new Error('hub connection refused')); } };
  });
}

/** Calls a hub method and waits for its answer: { result } or { error }. */
let hubCallSeq = 0;
async function hubCall(hub, target, ...args) {
  const invocationId = String(++hubCallSeq);
  hub.ws.send(JSON.stringify({ type: 1, invocationId, target, arguments: args }) + '\x1e');
  for (let i = 0; i < 40; i++) {
    if (hub.completions.has(invocationId)) return hub.completions.get(invocationId);
    await sleep(100);
  }
  return { error: 'no answer from the hub' };
}

const U1 = '00000001-0000-0000-0000-000000000000';
const U2 = '00000002-0000-0000-0000-000000000000';
const U3 = '00000003-0000-0000-0000-000000000000';
const NOBODY = '99999999-0000-0000-0000-000000000000';
const run = Date.now().toString(36);

// ───────────────────────── AUTH ─────────────────────────
console.log('\n== Auth ==');
const admin = await login('eslam@miqat.app');
const sarah = await login('sarah@miqat.app');
const ahmed = await login('ahmed@miqat.app');
const A = admin.accessToken, S = sarah.accessToken, H = ahmed.accessToken;
check('login returns tokens', !!A && !!admin.refreshToken);

let r = await call('POST', '/api/Auth/login', { body: { email: 'ESLAM@miqat.app', password: 'Test@1234' } });
check('login is case-insensitive on email', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email: 'eslam@miqat.app', password: 'Wrong@1234' } });
check('login wrong password -> 401', r.status === 401, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email: 'sarah@miqat.app', password: 'Test@1234' }, headers: { 'X-Forwarded-For': '203.0.113.1, 198.51.100.2, 192.0.2.3, 203.0.113.9, 10.0.0.1' } });
check('login with long X-Forwarded-For chain -> 200', r.status === 200, brief(r));
r = await call('GET', '/api/User/me');
check('no token -> 401', r.status === 401, brief(r));
r = await call('POST', '/api/Auth/refresh', { body: { refreshToken: admin.refreshToken } });
check('refresh -> 200 + new tokens', r.status === 200 && !!r.json?.accessToken, brief(r));
const rotated = r.json;
r = await call('POST', '/api/Auth/refresh', { body: { refreshToken: admin.refreshToken } });
check('reused refresh token -> 401', r.status === 401, brief(r));
r = await call('POST', '/api/Auth/logout', { body: { refreshToken: rotated.refreshToken } });
check('logout -> 200', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/refresh', { body: { refreshToken: rotated.refreshToken } });
check('refresh after logout -> 401', r.status === 401, brief(r));

const email = `T.${run}@Example.com`;
r = await call('POST', '/api/Auth/register', { body: { fullName: 'Test User', email, password: 'Passw0rd!x', timeZone: 'UTC' } });
check('register -> 200 (dev: code goes to the log)', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/register', { body: { fullName: 'Test User', email: email.toLowerCase(), password: 'Passw0rd!x' } });
check('register same email different case -> 409', r.status === 409, brief(r));
r = await call('POST', '/api/Auth/register', { body: { fullName: 'X', email: `long.${run}@example.com`, password: 'Passw0rd!x', phoneNumber: '1'.repeat(40), country: 'c'.repeat(150), timeZone: 't'.repeat(150) } });
check('register oversize fields -> 400 (not 500)', r.status === 400, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email, password: 'Passw0rd!x' } });
check('login before verification -> 401', r.status === 401, brief(r));
r = await call('POST', '/api/Auth/verify-otp', { body: { email, code: '000000', purpose: 'EmailVerification' } });
check('verify wrong otp -> 400', r.status === 400, brief(r));
const codesBeforeResend = devOtpLines(email, 'EmailVerification').length;
r = await call('POST', '/api/Auth/resend-otp', { body: { email, purpose: 'EmailVerification' } });
check('resend otp before verification -> 200 and a new code is issued', r.status === 200 && devOtpLines(email, 'EmailVerification').length === codesBeforeResend + 1, brief(r));
let code = otpFromLogs(email, 'EmailVerification');
r = await call('POST', '/api/Auth/verify-otp', { body: { email, code, purpose: 'EmailVerification' } });
check('verify otp -> 200 + tokens', r.status === 200 && !!r.json?.accessToken, `code=${code} ${brief(r)}`);
r = await call('POST', '/api/Auth/resend-otp', { body: { email, purpose: 'EmailVerification' } });
check('resend otp for verified account -> 400', r.status === 400, brief(r));

// forgot password, exactly as the web client does it: forgot -> verify-otp(PasswordReset) -> reset-password(token = the code)
r = await call('POST', '/api/Auth/forgot-password', { body: { email } });
check('forgot-password -> 200', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/forgot-password', { body: { email: 'nobody@nowhere.dev' } });
check('forgot-password unknown email -> 200 (no enumeration)', r.status === 200, brief(r));
code = otpFromLogs(email, 'PasswordReset');
r = await call('POST', '/api/Auth/verify-otp', { body: { email, code, purpose: 'PasswordReset' } });
check('verify-otp(PasswordReset) -> 200, no session token in the reply', r.status === 200 && r.json?.verified === true && r.json?.resetToken === code && !r.json?.accessToken && !r.json?.token && !r.json?.refreshToken, `code=${code} ${brief(r)}`);
r = await call('POST', '/api/Auth/verify-otp', { body: { email, code, purpose: 'PasswordReset' } });
check('verifying a reset code does not use it up', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/reset-password', { body: { email, token: code, newPassword: 'NewPassw0rd!', confirmPassword: 'NewPassw0rd!' } });
check('reset-password with the code -> 200', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/reset-password', { body: { email, token: code, newPassword: 'NewPassw0rd!2', confirmPassword: 'NewPassw0rd!2' } });
check('reset code is single use -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email, password: 'NewPassw0rd!' } });
check('login with new password -> 200', r.status === 200, brief(r));
const T2 = r.json?.accessToken;
const NEW = r.json?.userId;
r = await call('POST', '/api/Auth/reset-password', { body: { email: 'nobody@nowhere.dev', token: '123456', newPassword: 'NewPassw0rd!', confirmPassword: 'NewPassw0rd!' } });
check('reset-password unknown email -> 400 (no enumeration)', r.status === 400, brief(r));
r = await call('POST', '/api/Auth/change-password', { token: T2, body: { currentPassword: 'NewPassw0rd!', newPassword: 'a', confirmPassword: 'a' } });
check('change-password weak new password -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Auth/change-password', { token: T2, body: { currentPassword: 'bad', newPassword: 'Another1!x', confirmPassword: 'Another1!x' } });
check('change-password wrong current -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Auth/change-password', { token: T2, body: { currentPassword: 'NewPassw0rd!', newPassword: 'Another1!x', confirmPassword: 'Another1!x' } });
check('change-password -> 200', r.status === 200, brief(r));
r = await call('POST', '/api/Auth/google', { body: { token: 'garbage' } });
check('google invalid token -> 401', r.status === 401, brief(r));
r = await call('POST', '/api/Auth/login', { body: {} });
check('login empty body -> 400', r.status === 400, brief(r));

// ───────────────────────── USERS ─────────────────────────
console.log('\n== Users ==');
r = await call('GET', '/api/User/me', { token: S });
check('GET me -> 200', r.status === 200 && r.json?.email === 'sarah@miqat.app', brief(r));
const me = r.json;
r = await call('GET', `/api/User/${U3}`, { token: S });
check('GET another user -> 200, phone and birthday hidden', r.status === 200 && r.json?.phoneNumber == null && r.json?.dateOfBirth == null && !!r.json?.fullName, brief(r));
r = await call('GET', `/api/User/${U3}`, { token: A });
check('admin sees phone number', r.status === 200 && !!r.json?.phoneNumber, brief(r));
r = await call('GET', `/api/User/${NOBODY}`, { token: S });
check('GET unknown user -> 404', r.status === 404, brief(r));
r = await call('GET', '/api/User', { token: S });
check('GET all users as non-admin -> 403', r.status === 403, brief(r));
r = await call('GET', '/api/User', { token: A });
check('GET all users as admin -> 200', r.status === 200 && Array.isArray(r.json), brief(r));
r = await call('GET', '/api/User/search?query=ahmed', { token: S });
check('user search -> 200 list', r.status === 200 && Array.isArray(r.json) && r.json.length > 0, brief(r));
r = await call('GET', '/api/User/search', { token: S });
check('user search without query -> 400', r.status === 400, brief(r));
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: me.fullName, country: me.country, phoneNumber: me.phoneNumber, dateOfBirth: '1995-03-22', gender: 'Other', email: 'ahmed@miqat.app' } });
check('PUT me (plain date, no timeZone, gender Other) -> 204', r.status === 204, brief(r));
r = await call('GET', '/api/User/me', { token: S });
check('PUT me keeps timeZone, saves gender Other, ignores email', r.json?.timeZone === me.timeZone && r.json?.gender === 'Other' && r.json?.email === 'sarah@miqat.app', JSON.stringify(r.json).slice(0, 260));
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: me.fullName, dateOfBirth: '', gender: 'Female' } });
check('PUT me with empty-string date -> 204', r.status === 204, brief(r));
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: '   ' } });
check('PUT me blank name -> 400', r.status === 400, brief(r));

// bio
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: me.fullName, country: 'United States', phoneNumber: '+11234567890', gender: 'Female', bio: 'Marketing lead. Coffee first.' } });
check('PUT me with a bio -> 204', r.status === 204, brief(r));
r = await call('GET', '/api/User/me', { token: S });
check('bio is saved and returned', r.json?.bio === 'Marketing lead. Coffee first.', JSON.stringify(r.json).slice(0, 200));
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: me.fullName, country: 'United States', phoneNumber: '+11234567890', gender: 'Female' } });
r = await call('GET', '/api/User/me', { token: S });
check('a profile save without bio keeps the bio', r.json?.bio === 'Marketing lead. Coffee first.' && r.json?.country === 'United States', JSON.stringify(r.json).slice(0, 200));
r = await call('GET', '/api/User/' + U2, { token: H });
check('other people can read the bio', r.json?.bio === 'Marketing lead. Coffee first.', brief(r));
r = await call('PUT', '/api/User/me', { token: S, body: { fullName: me.fullName, bio: 'b'.repeat(501) } });
check('bio over 500 characters -> 400', r.status === 400, brief(r));

// notification preferences
r = await call('GET', '/api/User/me/preferences', { token: T2 });
check('preferences default to push on, email on, weekly off', r.status === 200 && r.json?.pushNotifications === true && r.json?.emailNotifications === true && r.json?.weeklySummary === false, brief(r));
r = await call('PUT', '/api/User/me/preferences', { token: T2, body: { weeklySummary: true, emailNotifications: false } });
check('save preferences -> 200 with the new values', r.status === 200 && r.json?.weeklySummary === true && r.json?.emailNotifications === false && r.json?.pushNotifications === true, brief(r));
r = await call('GET', '/api/User/me/preferences', { token: T2 });
check('preferences are stored', r.json?.weeklySummary === true && r.json?.emailNotifications === false && r.json?.pushNotifications === true, brief(r));
r = await call('GET', '/api/User/me/preferences');
check('preferences without token -> 401', r.status === 401, brief(r));

// contacts (people pickers)
r = await call('GET', '/api/User/contacts', { token: S });
check('contacts -> list of friends and teammates, never myself', r.status === 200 && Array.isArray(r.json) && r.json.length > 0 && !r.json.some(c => c.id === U2) && r.json.every(c => c.fullName && c.relationship && (c.isFriend || c.isTeammate)), brief(r));
r = await call('GET', '/api/User/contacts', { token: T2 });
check('a new account has no contacts yet', r.status === 200 && Array.isArray(r.json) && r.json.length === 0, brief(r));

// profile overview
r = await call('GET', '/api/User/' + U3 + '/profile', { token: S });
check('profile of someone else -> user, friendship, shared projects', r.status === 200 && r.json?.isMe === false && r.json?.user?.id === U3 && typeof r.json?.friendship?.status === 'string' && typeof r.json?.friendsCount === 'number' && typeof r.json?.mutualFriendsCount === 'number' && Array.isArray(r.json?.sharedProjects) && Array.isArray(r.json?.mutualFriends), brief(r));
check('profile hides the other person\'s phone number', r.json?.user?.phoneNumber == null, brief(r));
r = await call('GET', '/api/User/' + U2 + '/profile', { token: S });
check('my own profile -> isMe, private fields included', r.status === 200 && r.json?.isMe === true && r.json?.user?.phoneNumber === '+11234567890', brief(r));
r = await call('GET', '/api/User/' + NOBODY + '/profile', { token: S });
check('profile of unknown user -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/User/${U3}`, { token: S, body: me });
check('PUT other user as non-admin -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/User/${NEW}`, { token: A, body: { fullName: 'Test User Renamed', country: 'Egypt' } });
check('admin edit without email -> 204', r.status === 204, brief(r));
r = await call('PUT', `/api/User/${NEW}`, { token: A, body: { fullName: 'Test User Renamed', email: 'sarah@miqat.app' } });
check('admin edit with a taken email -> 409 (not 500)', r.status === 409, brief(r));
r = await call('DELETE', `/api/User/${U3}`, { token: S });
check('DELETE user as non-admin -> 403', r.status === 403, brief(r));
{
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
  let f = new FormData(); f.append('file', new Blob([png], { type: 'image/png' }), 'a.png');
  r = await call('POST', '/api/User/upload-profile-image', { token: T2, form: f });
  check('upload png -> 200 + url', r.status === 200 && !!r.json?.profileImageUrl, brief(r));
  r = await call('GET', '/api/User/me', { token: T2 });
  check('uploaded picture is on the profile', !!r.json?.profilePictureUrl, brief(r));
  f = new FormData(); f.append('file', new Blob(['hello'], { type: 'text/plain' }), 'a.txt');
  r = await call('POST', '/api/User/upload-profile-image', { token: T2, form: f });
  check('upload txt -> 400', r.status === 400, brief(r));
  f = new FormData(); f.append('file', new Blob(['<html>not an image</html>'], { type: 'image/png' }), 'fake.png');
  r = await call('POST', '/api/User/upload-profile-image', { token: T2, form: f });
  check('upload text renamed to .png -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/User/upload-profile-image', { token: T2, form: new FormData() });
  check('upload no file -> 400', r.status === 400, brief(r));
  r = await call('DELETE', '/api/User/profile-image', { token: T2 });
  check('remove profile image -> 204', r.status === 204, brief(r));
  r = await call('GET', '/api/User/me', { token: T2 });
  check('profile picture is gone', r.json?.profilePictureUrl == null, brief(r));
}
// admin access control
r = await call('PUT', `/api/User/${NEW}/access`, { token: S, body: { isActive: false } });
check('set access as non-admin -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/User/${NEW}/access`, { token: A, body: { role: 'Superuser' } });
check('set access with unknown role -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/User/${U1}/access`, { token: A, body: { isActive: false } });
check('admin cannot deactivate themselves -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/User/${NEW}/access`, { token: A, body: { isActive: false } });
check('admin deactivates a user -> 204', r.status === 204, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email, password: 'Another1!x' } });
check('deactivated user cannot login', r.status === 401, brief(r));
r = await call('PUT', `/api/User/${NEW}/access`, { token: A, body: { isActive: true } });
check('admin reactivates the user -> 204', r.status === 204, brief(r));
r = await call('POST', '/api/Auth/login', { body: { email, password: 'Another1!x' } });
check('reactivated user can login again', r.status === 200, brief(r));
const T = r.json?.accessToken || T2; // "stranger": a real account with no relationship to the seeded data

// ───────────────────────── TASKS ─────────────────────────
console.log('\n== Tasks ==');
r = await call('GET', '/api/Task', { token: S });
check('GET my tasks -> 200', r.status === 200 && Array.isArray(r.json), brief(r));
const sarahTasks = r.json || [];
check('task list has ownerName', sarahTasks.length > 0 && !!sarahTasks[0].ownerName, JSON.stringify(sarahTasks[0] || {}).slice(0, 200));

check('tasks carry the related fields (avatars, project colour, comment count, overdue flag)', ['ownerProfilePictureUrl', 'assignedToUserProfilePictureUrl', 'groupColor', 'commentCount', 'isOverdue', 'startDate', 'completedAt', 'updatedAt'].every(k => k in sarahTasks[0]), Object.keys(sarahTasks[0] || {}).join(','));
check('a task in a project carries the project colour', sarahTasks.filter(t => t.groupId).every(t => !!t.groupName && !!t.groupColor), JSON.stringify(sarahTasks.find(t => t.groupId) || {}).slice(0, 220));
check('isOverdue is true exactly for open tasks past their due date', sarahTasks.every(t => t.isOverdue === (!!t.dueDate && new Date(t.dueDate) < new Date() && t.status !== 'Completed' && t.status !== 'Cancelled')), '');
{
  const sum = (await call('GET', '/api/User/me/summary', { token: S })).json || {};
  const unreadCount = (await call('GET', '/api/Notification/unread-count', { token: S })).json?.unreadCount;
  const groupCount = ((await call('GET', '/api/Group', { token: S })).json || []).length;
  const t = sum.tasks || {};
  check('summary: task total matches my task list', t.total === sarahTasks.length, JSON.stringify(sum).slice(0, 300));
  check('summary: status counts add up to the total', t.pending + t.inProgress + t.onHold + t.completed + t.cancelled === t.total, JSON.stringify(t));
  check('summary: overdue matches the overdue flags', t.overdue === sarahTasks.filter(x => x.isOverdue).length, JSON.stringify(t));
  check('summary: projects, notifications and friends are counted', sum.projects === groupCount && sum.unreadNotifications === unreadCount && typeof sum.friends === 'number' && typeof sum.pendingFriendRequests === 'number' && typeof sum.unreadMentions === 'number' && !!sum.timeZone, JSON.stringify(sum).slice(0, 300));
}
r = await call('GET', '/api/Task/paged?pageIndex=0&pageSize=5', { token: S });
check('GET paged -> 200, items <= 5, totalCount', r.status === 200 && r.json?.items?.length <= 5 && r.json?.totalCount >= r.json?.items?.length && !!r.json?.items?.[0]?.ownerName, brief(r));
r = await call('GET', '/api/Task/paged?pageIndex=-1&pageSize=0', { token: S });
check('GET paged with bad paging -> 200 (clamped)', r.status === 200, brief(r));
r = await call('GET', '/api/Task/due-soon?withinDays=30', { token: S });
check('GET due-soon -> 200', r.status === 200 && Array.isArray(r.json), brief(r));
r = await call('GET', '/api/Task/all', { token: S });
check('GET all tasks non-admin -> 403', r.status === 403, brief(r));
r = await call('GET', '/api/Task/all', { token: A });
check('GET all tasks admin -> 200 with names', r.status === 200 && !!r.json?.[0]?.ownerName, brief(r));

const future = new Date(Date.now() + 5 * 864e5).toISOString();
r = await call('POST', '/api/Task', { token: S, body: { title: `t-${run}`, priority: 'High', status: 'Pending', dueDate: future, description: 'zebra crossing notes' } });
check('create task -> 201 with ownerName', r.status === 201 && !!r.json?.id && !!r.json?.ownerName, brief(r));
const task = r.json || {};
r = await call('POST', '/api/Task', { token: S, body: { title: `t2-${run}`, priority: 'Low', dueDate: future.slice(0, 16) } });
check('create task with offset-less dueDate -> 201 (not 500)', r.status === 201, brief(r));
r = await call('POST', '/api/Task', { token: S, body: { title: `today-${run}`, priority: 'Low', dueDate: new Date().toISOString().slice(0, 10) } });
check('create task due today (date only) -> 201', r.status === 201, brief(r));
r = await call('POST', '/api/Task', { token: S, body: { title: 'old', priority: 'Low', dueDate: new Date(Date.now() - 10 * 864e5).toISOString() } });
check('create task due 10 days ago -> 400', r.status === 400, brief(r));

{
  const start = new Date(Date.now() + 2 * 864e5).toISOString(), due = new Date(Date.now() + 3 * 864e5).toISOString();
  r = await call('POST', '/api/Task', { token: S, body: { title: 'range-' + run, priority: 'Low', startDate: start, dueDate: due } });
  check('create task with a start date -> 201, start and due returned', r.status === 201 && new Date(r.json?.startDate).getTime() === new Date(start).getTime() && new Date(r.json?.dueDate).getTime() === new Date(due).getTime(), brief(r));
  const ranged = r.json || {};
  r = await call('POST', '/api/Task', { token: S, body: { title: 'bad-range', priority: 'Low', startDate: due, dueDate: start } });
  check('start date after due date -> 400', r.status === 400, brief(r));
  r = await call('PUT', '/api/Task/' + ranged.id, { token: S, body: { ...ranged, startDate: null } });
  r = await call('GET', '/api/Task/' + ranged.id, { token: S });
  check('start date can be cleared', r.status === 200 && r.json?.startDate === null, brief(r));
  // filters
  r = await call('GET', '/api/Task?priority=Low&dueFrom=' + encodeURIComponent(start.slice(0, 10)) + '&dueTo=' + encodeURIComponent(new Date(Date.now() + 4 * 864e5).toISOString()), { token: S });
  check('filter by priority + due range finds the task', r.status === 200 && r.json.some(t => t.id === ranged.id) && r.json.every(t => t.priority === 'Low' && t.dueDate), brief(r));
  r = await call('GET', '/api/Task?dueFrom=' + encodeURIComponent(new Date(Date.now() + 300 * 864e5).toISOString()), { token: S });
  check('filter with a far-future range -> empty list', r.status === 200 && r.json.length === 0, brief(r));
  r = await call('GET', '/api/Task?status=Pending', { token: S });
  check('filter by status returns only that status', r.status === 200 && r.json.length > 0 && r.json.every(t => t.status === 'Pending'), brief(r));
  r = await call('GET', '/api/Task?status=InProgress', { token: S });
  check('status filter accepts the InProgress spelling', r.status === 200 && r.json.every(t => t.status === 'In_progress'), brief(r));
  r = await call('GET', '/api/Task?status=Sleeping', { token: S });
  check('filter with unknown status -> 400', r.status === 400, brief(r));
  r = await call('GET', '/api/Task?dueFrom=2027-01-02&dueTo=2027-01-01', { token: S });
  check('filter with a reversed range -> 400', r.status === 400, brief(r));
  const inGroup = sarahTasks.find(t => t.groupId);
  if (inGroup) {
    r = await call('GET', '/api/Task?groupId=' + inGroup.groupId, { token: S });
    check('filter by project returns only that project\'s tasks', r.status === 200 && r.json.length > 0 && r.json.every(t => t.groupId === inGroup.groupId), brief(r));
  }
}
r = await call('POST', '/api/Task', { token: S, body: { title: '', priority: 'High' } });
check('create task empty title -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Task', { token: S, body: { title: 'x', priority: 'Urgent' } });
check('create task unknown priority -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Task', { token: S, body: { title: 'x', priority: 'High', groupId: NOBODY } });
check('create task unknown group -> 4xx (not 500)', r.status >= 400 && r.status < 500, brief(r));
r = await call('POST', '/api/Task', { token: S, body: { title: 'x', priority: 'High', assignedToUserId: NOBODY } });
check('create task unknown assignee -> 400 (not 500)', r.status === 400, brief(r));

r = await call('PUT', `/api/Task/${task.id}`, { token: S, body: { ...task, status: 'InProgress' } });
check('update status with "InProgress" spelling -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Task/${task.id}`, { token: S });
check('status stored, returned as In_progress', r.json?.status === 'In_progress', `status=${r.json?.status}`);
r = await call('PUT', `/api/Task/${task.id}`, { token: S, body: { ...r.json, title: `t-${run} renamed`, status: 'On_hold' } });
check('round-trip PUT of a GET payload (On_hold) -> 204', r.status === 204, brief(r));
r = await call('PUT', `/api/Task/${task.id}`, { token: S, body: { ...task, status: 'Sleeping' } });
check('update with unknown status -> 400', r.status === 400, brief(r));
// body without id (what a plain edit form sends) must still work
r = await call('PUT', `/api/Task/${task.id}`, { token: S, body: { title: `t-${run}`, priority: 'High', status: 'Pending', dueDate: future, description: 'zebra crossing notes', assignedToUserId: U3 } });
check('assign task to ahmed (body without id) -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Task/${task.id}`, { token: H });
check('assignee can read the task, sees names', r.status === 200 && r.json?.assignedToUserId === U3 && !!r.json?.assignedToUserName, brief(r));
let n = (await notifs(H)).find(x => x.linkedEntityId === task.id && x.type === 'TaskAssigned');
check('assignee got a TaskAssigned notification', !!n && n.triggeredByUserName === me.fullName, JSON.stringify(n || {}).slice(0, 220));

check('the notification names the task it is about', n?.linkedEntityName === 't-' + run && 'triggeredByUserProfilePictureUrl' in (n || {}), JSON.stringify(n || {}).slice(0, 260));
r = await call('GET', '/api/Task', { token: H });
check('assigned task appears in assignee\'s own list', (r.json || []).some(t => t.id === task.id), brief(r));
r = await call('PUT', `/api/Task/${task.id}`, { token: H, body: { title: `t-${run}`, priority: 'High', status: 'Completed', dueDate: future, description: 'zebra crossing notes', assignedToUserId: U3 } });
check('assignee marks it Completed -> 204', r.status === 204, brief(r));
n = (await notifs(S)).find(x => x.linkedEntityId === task.id && x.type === 'TaskCompleted');
check('owner got a TaskCompleted notification', !!n, JSON.stringify(n || {}).slice(0, 220));

r = await call('GET', '/api/Task/' + task.id, { token: S });
check('completing a task records completedAt', r.json?.status === 'Completed' && !!r.json?.completedAt && Math.abs(Date.now() - new Date(r.json.completedAt).getTime()) < 60000 && r.json?.isOverdue === false, brief(r));
r = await call('PUT', '/api/Task/' + task.id, { token: S, body: { ...r.json, status: 'In_progress' } });
r = await call('GET', '/api/Task/' + task.id, { token: S });
check('reopening a task clears completedAt', r.json?.status === 'In_progress' && r.json?.completedAt === null, brief(r));
const overdue = sarahTasks.find(t => t.userId === U2 && t.dueDate && new Date(t.dueDate) < new Date());
if (overdue) {
  r = await call('PUT', `/api/Task/${overdue.id}`, { token: S, body: { ...overdue, status: 'Completed' } });
  check('overdue task can be marked Completed', r.status === 204, brief(r));
} else console.log('SKIP  no overdue seeded task owned by sarah');

r = await call('GET', `/api/Task/${task.id}`, { token: T });
check('stranger cannot read someone else\'s task', denied(r), brief(r));
r = await call('PUT', `/api/Task/${task.id}`, { token: T, body: { ...task, title: 'hacked' } });
check('stranger cannot update someone else\'s task', denied(r), brief(r));
r = await call('DELETE', `/api/Task/${task.id}`, { token: T });
check('stranger cannot delete someone else\'s task', denied(r), brief(r));
r = await call('DELETE', `/api/Task/${task.id}`, { token: H });
check('assignee cannot delete the owner\'s task', r.status === 403, brief(r));
r = await call('GET', `/api/Task/${NOBODY}`, { token: S });
check('unknown task -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Task/${NOBODY}`, { token: S, body: { title: 'x', priority: 'Low' } });
check('update unknown task -> 404', r.status === 404, brief(r));

// ───────────────────────── COMMENTS ─────────────────────────
console.log('\n== Comments ==');
r = await call('GET', `/api/Task/${task.id}/comments`, { token: S });
check('GET comments -> 200 []', r.status === 200 && Array.isArray(r.json) && r.json.length === 0, brief(r));
r = await call('POST', `/api/Task/${task.id}/comments`, { token: S, body: { content: 'first comment', mentionedUserIds: [NEW] } });
check('add comment -> 201', r.status === 201 && !!r.json?.id && r.json?.authorName === me.fullName, brief(r));

check('a new comment is not marked edited', r.json?.isEdited === false, brief(r));
const comment = r.json || {};
n = (await notifs(H)).find(x => x.linkedCommentId === comment.id);
check('assignee notified about the comment (with linkedCommentId)', !!n && n.linkedEntityId === task.id, JSON.stringify(n || {}).slice(0, 220));
n = (await notifs(T)).find(x => x.linkedCommentId === comment.id);
check('mentioning an unrelated user by id notifies nobody', !n, JSON.stringify(n || {}).slice(0, 220));
r = await call('GET', `/api/Task/${task.id}/mentionable`, { token: S });
check('mentionable lists the assignee', r.status === 200 && (r.json || []).some(u => u.userId === U3 && u.hasAccess === true), brief(r));
r = await call('POST', `/api/Task/${task.id}/comments`, { token: H, body: { content: 'reply from ahmed' } });
check('assignee can comment -> 201', r.status === 201, brief(r));
const reply = r.json || {};
r = await call('POST', `/api/Task/${task.id}/comments`, { token: T, body: { content: 'intruder' } });
check('stranger cannot comment', denied(r), brief(r));
r = await call('GET', `/api/Task/${task.id}/comments`, { token: T });
check('stranger cannot read comments', denied(r), brief(r));
r = await call('POST', `/api/Task/${task.id}/comments`, { token: S, body: { content: '   ' } });
check('empty comment -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Task/${task.id}/comments`, { token: S, body: { content: 'x'.repeat(2001) } });
check('over-long comment -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Task/comments/${comment.id}`, { token: S, body: { content: 'first comment (edited)' } });
check('author edits comment -> 200', r.status === 200 && r.json?.content === 'first comment (edited)' && !!r.json?.updatedAt, brief(r));

check('an edited comment is marked edited', r.json?.isEdited === true, brief(r));
r = await call('PUT', `/api/Task/comments/${comment.id}`, { token: H, body: { content: 'not mine' } });
check('non-author cannot edit -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/Task/comments/${NOBODY}`, { token: S, body: { content: 'x' } });
check('edit unknown comment -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Task/comments/${comment.id}`, { token: H });
check('non-author cannot delete -> 403', r.status === 403, brief(r));
r = await call('DELETE', `/api/Task/comments/${reply.id}`, { token: H });
check('author deletes own comment -> 204', r.status === 204, brief(r));
r = await call('DELETE', `/api/Task/comments/${reply.id}`, { token: H });
check('delete again -> 404', r.status === 404, brief(r));
r = await call('GET', `/api/Task/${task.id}/comments`, { token: H });
check('thread shows 1 remaining comment, oldest first', r.status === 200 && r.json.length === 1 && r.json[0].id === comment.id, brief(r));

r = await call('GET', '/api/Task/' + task.id, { token: S });
check('the task reports its comment count (deleted ones excluded)', r.json?.commentCount === 1, 'commentCount=' + r.json?.commentCount);
r = await call('GET', '/api/Task', { token: S });
check('the comment count is on the task list too', r.json.find(t => t.id === task.id)?.commentCount === 1, '');

// ───────────────────────── REALTIME ─────────────────────────
console.log('\n== Realtime (SignalR) ==');
{
  let refused = false;
  try { const c = await connectHub(null); c.ws.close(); } catch { refused = true; }
  check('hub refuses a connection without a token', refused);
  let hub = null;
  try { hub = await connectHub(H); } catch (e) { check('hub accepts a signed-in connection', false, String(e)); }
  if (hub) {
    check('hub accepts a signed-in connection', true);
    r = await call('POST', '/api/Task', { token: S, body: { title: `live-${run}`, priority: 'Medium', assignedToUserId: U3 } });
    const liveId = r.json?.id;
    await sleep(1500);
    const got = hub.events;
    check('assignee\'s open tab receives "taskChanged" live', got.some(e => e.target === 'taskChanged' && e.args[0]?.taskId === liveId && e.args[0]?.action === 'created'), JSON.stringify(got).slice(0, 300));
    check('assignee\'s open tab receives the "notification" live', got.some(e => e.target === 'notification' && e.args[0]?.type === 'TaskAssigned' && e.args[0]?.linkedEntityId === liveId), JSON.stringify(got).slice(0, 300));
    r = await call('POST', `/api/Task/${liveId}/comments`, { token: S, body: { content: 'ping' } });
    await sleep(1200);
    check('assignee\'s open tab receives "commentAdded" live', got.some(e => e.target === 'commentAdded' && e.args[0]?.taskId === liveId), JSON.stringify(got).slice(0, 300));

    // push notifications switched off: no pop-up, but nothing is lost
    r = await call('PUT', '/api/User/me/preferences', { token: H, body: { pushNotifications: false } });
    r = await call('POST', '/api/Task', { token: S, body: { title: 'quiet-' + run, priority: 'Medium', assignedToUserId: U3 } });
    const quietId = r.json?.id;
    await sleep(1500);
    check('with push off, no live "notification" is sent', !got.some(e => e.target === 'notification' && e.args[0]?.linkedEntityId === quietId), JSON.stringify(got.filter(e => e.target === 'notification')).slice(0, 300));
    check('with push off, the screen still updates ("taskChanged")', got.some(e => e.target === 'taskChanged' && e.args[0]?.taskId === quietId), '');
    n = (await notifs(H)).find(x => x.linkedEntityId === quietId && x.type === 'TaskAssigned');
    check('with push off, the notification is still in the feed', !!n, JSON.stringify(n || {}).slice(0, 200));
    r = await call('PUT', '/api/User/me/preferences', { token: H, body: { pushNotifications: true } });
    check('push switched back on', r.json?.pushNotifications === true, brief(r));
    hub.ws.close();
  }
}

// ───────────────────────── GROUPS / PROJECTS ─────────────────────────
console.log('\n== Projects ==');
r = await call('GET', '/api/Group', { token: S });
check('GET my projects -> 200', r.status === 200 && Array.isArray(r.json), brief(r));
const sarahGroups = r.json || [];
check('project list has ownerName + memberCount', sarahGroups.length > 0 && sarahGroups.every(g => !!g.ownerName && g.memberCount >= 1), JSON.stringify(sarahGroups[0] || {}).slice(0, 240));
r = await call('POST', '/api/Group', { token: S, body: { name: `g-${run} quokka`, description: 'd', color: '#FF5733' } });
check('create project -> 201 with ownerName, 1 member', r.status === 201 && !!r.json?.id && !!r.json?.ownerName && r.json?.memberCount === 1, brief(r));
const group = r.json || {};
r = await call('POST', '/api/Group', { token: S, body: { name: '', color: 'red' } });
check('create project invalid -> 400', r.status === 400, brief(r));
r = await call('GET', `/api/Group/${group.id}`, { token: S });
check('GET project -> 200', r.status === 200 && !!r.json?.ownerName, brief(r));

check('project tells its owner "this is yours"', r.json?.isOwner === true && 'ownerProfilePictureUrl' in r.json && 'updatedAt' in r.json, brief(r));
r = await call('GET', `/api/Group/${group.id}`, { token: T });
check('stranger cannot read a project', denied(r), brief(r));
r = await call('PUT', `/api/Group/${group.id}`, { token: T, body: { name: 'hacked' } });
check('stranger cannot update a project', denied(r), brief(r));
r = await call('POST', `/api/Group/${group.id}/members/${U3}`, { token: T });
check('stranger cannot add members', denied(r), brief(r));
r = await call('DELETE', `/api/Group/${group.id}`, { token: T });
check('stranger cannot delete a project', denied(r), brief(r));
r = await call('GET', `/api/Group/${group.id}/members`, { token: T });
check('stranger cannot list members', denied(r), brief(r));
r = await call('GET', `/api/Task/group/${group.id}`, { token: T });
check('stranger cannot list project tasks', denied(r), brief(r));
r = await call('POST', `/api/Group/${group.id}/members/${U3}`, { token: S });
check('owner adds member -> 200', r.status === 200, brief(r));
n = (await notifs(H)).find(x => x.linkedEntityId === group.id && x.type === 'GroupInvite');
check('new member got a GroupInvite notification', !!n, JSON.stringify(n || {}).slice(0, 200));
r = await call('POST', `/api/Group/${group.id}/members/${U3}`, { token: S });
check('add duplicate member -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Group/${group.id}/members/${NOBODY}`, { token: S });
check('add unknown user -> 404 (not 500)', r.status === 404, brief(r));
r = await call('POST', `/api/Group/${NOBODY}/members/${U3}`, { token: S });
check('add member to unknown project -> 404 (not 500)', r.status === 404, brief(r));
r = await call('GET', `/api/Group/${group.id}/members?pageIndex=0&pageSize=10`, { token: H });
check('member lists members: owner + member, total 2', r.status === 200 && r.json?.items?.length === 2 && r.json?.totalCount === 2 && r.json.items[0].userId === U2, brief(r));

check('member list marks who the owner is', r.json?.items?.[0]?.role === 'Owner' && r.json.items[0].isOwner === true && r.json.items[1].role === 'Member' && r.json.items[1].isOwner === false, brief(r));
r = await call('GET', '/api/Group/' + group.id, { token: H });
check('a member sees isOwner = false', r.status === 200 && r.json?.isOwner === false, brief(r));
r = await call('GET', '/api/User/contacts', { token: H });
check('a project teammate appears in contacts', r.json?.some(c => c.id === U2 && c.isTeammate === true), brief(r));
r = await call('GET', '/api/User/' + U2 + '/profile', { token: H });
check('profile lists the project we share', r.json?.sharedProjects?.some(p => p.id === group.id), brief(r));
r = await call('GET', `/api/Group/${group.id}/members?pageIndex=-3&pageSize=10`, { token: S });
check('members negative page -> 200 (clamped)', r.status === 200, brief(r));
r = await call('PUT', `/api/Group/${group.id}`, { token: H, body: { name: 'member-edit' } });
check('plain member cannot update project -> 403', r.status === 403, brief(r));
r = await call('POST', '/api/Task', { token: H, body: { title: `gt-${run}`, priority: 'Medium', groupId: group.id } });
check('member creates project task -> 201 with groupName', r.status === 201 && !!r.json?.groupName, brief(r));
const groupTask = r.json || {};

check('the project task carries the project colour', r.json?.groupColor === '#FF5733', brief(r));
r = await call('GET', '/api/User/' + U3 + '/profile', { token: S });
check('shared project counts the other person\'s tasks in it', r.json?.sharedProjects?.find(p => p.id === group.id)?.taskCount === 1, brief(r));
r = await call('POST', '/api/Task', { token: T, body: { title: 'intruder', priority: 'Medium', groupId: group.id } });
check('stranger cannot create task in project', denied(r), brief(r));
r = await call('GET', `/api/Task/group/${group.id}`, { token: S });
check('project tasks -> 200 with 1 task', r.status === 200 && r.json.length === 1, brief(r));
r = await call('PUT', `/api/Task/${groupTask.id}`, { token: S, body: { ...groupTask, status: 'Completed' } });
check('project owner can edit a member\'s task -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Group/${group.id}`, { token: S });
check('project counts: 1 task, 1 completed, 2 members', r.json?.taskCount === 1 && r.json?.completedTaskCount === 1 && r.json?.memberCount === 2, brief(r));
r = await call('PUT', `/api/Group/${group.id}`, { token: S, body: { name: `g2-${run} quokka`, color: '#000000' } });
check('owner updates project -> 204', r.status === 204, brief(r));

// ───────────────────────── SEARCH ─────────────────────────
console.log('\n== Global search ==');
r = await call('GET', `/api/Search?query=${encodeURIComponent('t-' + run)}`, { token: S });
check('search finds my task by title', r.status === 200 && r.json?.tasks?.some(t => t.id === task.id), brief(r));
r = await call('GET', '/api/Search?query=ZEBRA', { token: S });
check('search matches the description, case-insensitive', r.status === 200 && r.json?.tasks?.some(t => t.id === task.id), brief(r));
r = await call('GET', '/api/Search?query=quokka', { token: H });
check('search finds a project I am a member of', r.status === 200 && r.json?.projects?.some(p => p.id === group.id), brief(r));
r = await call('GET', '/api/Search?query=ahmed', { token: S });
check('search finds people', r.status === 200 && r.json?.users?.some(u => u.id === U3), brief(r));
r = await call('GET', `/api/Search?query=${encodeURIComponent('t-' + run)}`, { token: T });
check('search never returns other people\'s tasks', r.status === 200 && r.json?.tasks?.length === 0, brief(r));
r = await call('GET', '/api/Search?query=quokka', { token: T });
check('search never returns other people\'s projects', r.status === 200 && r.json?.projects?.length === 0, brief(r));
r = await call('GET', '/api/Search?query=a', { token: S });
check('search with 1 character -> 400', r.status === 400, brief(r));
r = await call('GET', '/api/Search?query=ahmed');
check('search without token -> 401', r.status === 401, brief(r));

// finish the project lifecycle
r = await call('DELETE', `/api/Group/${group.id}/members/${U3}`, { token: H });
check('member can leave project -> 200', r.status === 200, brief(r));
r = await call('DELETE', `/api/Group/${group.id}/members/${U3}`, { token: S });
check('remove non-member -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Group/${group.id}`, { token: S });
check('owner deletes project -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Group/${group.id}`, { token: S });
check('deleted project -> 404', r.status === 404, brief(r));
r = await call('GET', `/api/Group/${group.id}/members`, { token: S });
check('members of deleted project -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Group/${group.id}`, { token: S });
check('delete project again -> 404', r.status === 404, brief(r));

// task deletion
r = await call('DELETE', `/api/Task/${task.id}`, { token: S });
check('owner deletes task -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Task/${task.id}`, { token: S });
check('deleted task -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Task/${task.id}`, { token: S });
check('delete already-deleted task -> 404', r.status === 404, brief(r));
r = await call('GET', `/api/Task/${task.id}/comments`, { token: S });
check('comments of a deleted task are not readable', denied(r), brief(r));

// ───────────────────────── BOARDS ─────────────────────────
console.log('\n== Boards (whiteboard / node diagram) ==');
r = await call('GET', '/api/Board/latest?kind=Whiteboard', { token: T });
check('latest board when none exists -> 204', r.status === 204, brief(r));
r = await call('POST', '/api/Board', { token: T, body: { kind: 'Whiteboard', content: '{"strokes":[1,2,3]}' } });
check('create board -> 201 with default name', r.status === 201 && !!r.json?.id && r.json?.name === 'Untitled board', brief(r));
const board = r.json || {};
r = await call('POST', '/api/Board', { token: T, body: { kind: 'NodeFlow', name: 'My flow', content: '{"nodes":[]}' } });
check('create node diagram -> 201', r.status === 201 && r.json?.kind === 'NodeFlow', brief(r));
r = await call('POST', '/api/Board', { token: T, body: { kind: 'Whiteboard', content: 'this is not json' } });
check('create board with invalid JSON content -> 400 (not 500)', r.status === 400, brief(r));
r = await call('POST', '/api/Board', { token: T, body: { kind: 'Whiteboard', name: 'n'.repeat(200), content: '{}' } });
check('create board with over-long name -> 400 (not 500)', r.status === 400, brief(r));
r = await call('GET', `/api/Board/${board.id}`, { token: T });
check('GET board -> 200 with content', r.status === 200 && JSON.parse(r.json?.content || '{}').strokes?.length === 3, brief(r));
r = await call('GET', `/api/Board/${board.id}`, { token: S });
check('someone else cannot read my board -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Board/${board.id}`, { token: S, body: { kind: 'Whiteboard', content: '{}' } });
check('someone else cannot overwrite my board -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Board/${board.id}`, { token: T, body: { kind: 'Whiteboard', name: 'Renamed', content: '{"strokes":[1]}' } });
check('update board -> 200', r.status === 200 && r.json?.name === 'Renamed' && !!r.json?.updatedAt, brief(r));
r = await call('PUT', `/api/Board/${board.id}`, { token: T, body: { kind: 'Whiteboard', content: '{broken' } });
check('update board with invalid JSON -> 400', r.status === 400, brief(r));
r = await call('GET', '/api/Board?kind=Whiteboard', { token: T });
check('list boards by kind -> 1 whiteboard', r.status === 200 && r.json.length === 1, brief(r));
r = await call('GET', '/api/Board', { token: T });
check('list all my boards -> 2', r.status === 200 && r.json.length === 2, brief(r));
r = await call('GET', '/api/Board/latest?kind=Whiteboard', { token: T });
check('latest whiteboard -> 200', r.status === 200 && r.json?.id === board.id, brief(r));
r = await call('DELETE', `/api/Board/${board.id}`, { token: S });
check('someone else cannot delete my board -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Board/${board.id}`, { token: T });
check('delete board -> 204', r.status === 204, brief(r));
r = await call('DELETE', `/api/Board/${board.id}`, { token: T });
check('delete board again -> 404', r.status === 404, brief(r));

// ───────────────────────── FRIENDS ─────────────────────────
console.log('\n== Friends ==');
r = await call('GET', '/api/Friends', { token: S });
check('GET friends -> 200', r.status === 200 && Array.isArray(r.json?.friends), brief(r));
r = await call('GET', '/api/Friends/my-friends', { token: S });
check('GET friends/my-friends -> 200 array', r.status === 200 && Array.isArray(r.json), brief(r));
r = await call('GET', '/api/Friends/pending', { token: S });
check('GET pending -> 200', r.status === 200 && Array.isArray(r.json?.pendingRequests), brief(r));
r = await call('GET', '/api/Friends/sent', { token: S });
check('GET sent -> 200', r.status === 200 && Array.isArray(r.json?.sentRequests), brief(r));
r = await call('GET', '/api/Friends/suggestions', { token: S });
check('GET suggestions -> 200 array', r.status === 200 && Array.isArray(r.json), brief(r));
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: S });
check('friend request to self -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Friends/send-request/${NOBODY}`, { token: S });
check('friend request to unknown user -> 404', r.status === 404, brief(r));
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: T });
check('send request -> 200', r.status === 200 && !!r.json?.friendship?.id, brief(r));
let fid = r.json?.friendship?.id;
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: T });
check('duplicate request -> 400', r.status === 400, brief(r));
r = await call('GET', `/api/Friends/status/${U2}`, { token: T });
check('status Sent + id for sender', r.json?.status === 'Sent' && r.json?.id === fid, brief(r));
r = await call('GET', `/api/Friends/status/${NEW}`, { token: S });
check('status Pending for receiver', r.json?.status === 'Pending', brief(r));
n = (await notifs(S)).find(x => x.linkedEntityId === fid);
check('friend request notification has type FriendRequestSent', n?.type === 'FriendRequestSent', JSON.stringify(n || {}).slice(0, 200));
r = await call('GET', `/api/Friends/${fid}`, { token: H });
check('third party cannot read a friendship', denied(r), brief(r));
r = await call('GET', `/api/Friends/${fid}`, { token: T });
check('sender can read the friendship -> 200', r.status === 200 && r.json?.id === fid, brief(r));
r = await call('GET', `/api/Friends/${fid}`, { token: S });
check('receiver can read the friendship -> 200', r.status === 200 && r.json?.id === fid, brief(r));
r = await call('PUT', `/api/Friends/accept/${fid}`, { token: T });
check('sender cannot accept own request -> 4xx', r.status >= 400 && r.status < 500, brief(r));
r = await call('PUT', `/api/Friends/reject/${fid}`, { token: H });
check('third party reject -> 4xx (not 500)', r.status >= 400 && r.status < 500, brief(r));
r = await call('PUT', `/api/Friends/accept/${fid}`, { token: S });
check('receiver accepts -> 200', r.status === 200, brief(r));
r = await call('PUT', `/api/Friends/accept/${fid}`, { token: S });
check('accept twice -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Friends/reject/${fid}`, { token: S });
check('reject an accepted friendship -> 400', r.status === 400, brief(r));
r = await call('GET', `/api/Friends/status/${U2}`, { token: T });
check('status Friends', r.json?.status === 'Friends', brief(r));

r = await call('GET', '/api/Friends', { token: T });
let row = (r.json?.friends || []).find(f => f.id === fid);
check('friend row names the other person (for the sender)', row?.friendId === U2 && row?.friendName === me.fullName && row?.friendUser?.id === U2 && row?.sender?.id === NEW && row?.receiver?.id === U2 && row?.sentByMe === true, JSON.stringify(row || {}).slice(0, 300));
r = await call('GET', '/api/Friends/my-friends', { token: S });
row = (r.json || []).find(f => f.id === fid);
check('friend row names the other person (for the receiver)', row?.friendId === NEW && row?.friendUser?.id === NEW && row?.sentByMe === false, JSON.stringify(row || {}).slice(0, 300));
r = await call('GET', '/api/User/' + U2 + '/profile', { token: T });
check('profile shows we are friends, with the friendship id', r.json?.friendship?.status === 'Friends' && r.json?.friendship?.id === fid, brief(r));
r = await call('GET', '/api/User/contacts', { token: T });
check('a friend appears in contacts', r.json?.some(c => c.id === U2 && c.isFriend === true), brief(r));
r = await call('GET', '/api/User/me/summary', { token: T });
check('summary counts the new friend', r.json?.friends === 1, brief(r));
n = (await notifs(T)).find(x => x.linkedEntityId === fid && x.type === 'FriendRequestAccepted');
check('sender got a FriendRequestAccepted notification', !!n, JSON.stringify(n || {}).slice(0, 200));
r = await call('DELETE', `/api/Friends/${fid}`, { token: H });
check('third party cannot unfriend -> 4xx', r.status >= 400 && r.status < 500, brief(r));
r = await call('DELETE', `/api/Friends/${fid}`, { token: T });
check('unfriend -> 200', r.status === 200, brief(r));
r = await call('GET', `/api/Friends/status/${U2}`, { token: T });
check('status None after unfriend', r.json?.status === 'None', brief(r));
r = await call('POST', `/api/Friends/send-request/${NEW}`, { token: S });
check('can send a new request after unfriend (reverse direction)', r.status === 200, brief(r));
fid = r.json?.friendship?.id;
r = await call('PUT', `/api/Friends/reject/${fid}`, { token: T });
check('receiver rejects -> 200', r.status === 200, brief(r));
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: T });
check('can send request again after a rejection', r.status === 200, brief(r));
r = await call('PUT', `/api/Friends/block/${NEW}`, { token: S });
check('block -> 200', r.status === 200, brief(r));
r = await call('GET', `/api/Friends/status/${NEW}`, { token: S });
check('status Blocked for blocker', r.json?.status === 'Blocked', brief(r));
r = await call('GET', '/api/Friends/blocked', { token: S });
check('blocked list shows the blocked user', r.status === 200 && r.json?.blockedUsers?.some(u => u.id === NEW), brief(r));
r = await call('GET', `/api/Friends/status/${U2}`, { token: T });
check('blocked user is not told about the block', r.json?.status === 'None', brief(r));
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: T });
check('blocked user cannot send request', r.status === 400, brief(r));
r = await call('PUT', `/api/Friends/unblock/${U2}`, { token: T });
check('blocked user cannot unblock themselves', r.status >= 400 && r.status < 500, brief(r));
r = await call('GET', `/api/Friends/status/${NEW}`, { token: S });
check('still Blocked after victim\'s unblock attempt', r.json?.status === 'Blocked', brief(r));
r = await call('PUT', `/api/Friends/unblock/${NEW}`, { token: S });
check('blocker unblocks -> 200', r.status === 200, brief(r));
r = await call('GET', '/api/Friends/blocked', { token: S });
check('blocked list is empty again', r.status === 200 && !r.json?.blockedUsers?.some(u => u.id === NEW), brief(r));
r = await call('GET', `/api/Friends/status/${NEW}`, { token: S });
check('status None after unblock', r.json?.status === 'None', brief(r));
r = await call('POST', `/api/Friends/send-request/${U2}`, { token: T });
check('can send request after unblock', r.status === 200, brief(r));
r = await call('PUT', `/api/Friends/block/${U2}`, { token: S });
check('block self -> 400', r.status === 400, brief(r));

// ───────────────────────── MENTIONS ─────────────────────────
console.log('\n== Mentions ==');
r = await call('GET', '/api/Mentions', { token: S });
check('GET mentions -> 200', r.status === 200 && Array.isArray(r.json?.mentions), brief(r));
r = await call('GET', '/api/Mentions/unread', { token: S });
check('GET unread mentions -> 200', r.status === 200 && Array.isArray(r.json?.unreadMentions), brief(r));
const unreadMentions = r.json?.unreadMentions?.length;
r = await call('GET', '/api/Mentions/unread-count', { token: S });
check('mentions unread-count matches the unread list', r.status === 200 && r.json?.unreadCount === unreadMentions, brief(r));
r = await call('POST', '/api/Mentions/parse', { token: S, body: { text: 'hello @Ahmed and @nobodyzzz' } });
check('parse -> 200 with 1 id', r.status === 200 && r.json?.mentionedUserIds?.length === 1, brief(r));
r = await call('POST', '/api/Mentions/parse', { token: S, body: { text: '' } });
check('parse empty -> 400', r.status === 400, brief(r));
const ent = crypto.randomUUID();
r = await call('POST', '/api/Mentions/create', { token: S, body: { mentionedUserIds: [U3, U2, NOBODY], entityType: 1, entityId: ent } });
check('create mentions -> 200, 1 created (self + unknown skipped)', r.status === 200 && r.json?.mentions?.length === 1, brief(r));
const mid = r.json?.mentions?.[0]?.id;
r = await call('POST', '/api/Mentions/create', { token: S, body: { mentionedUserIds: [U3], entityType: 'Task', entityId: ent } });
check('create accepts entityType by name; duplicate is skipped', r.status === 200 && r.json?.mentions?.length === 0, brief(r));
r = await call('POST', '/api/Mentions/create', { token: S, body: { mentionedUserIds: [U3], entityType: 99, entityId: ent } });
check('create invalid entityType -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Mentions/create', { token: S, body: { mentionedUserIds: [], entityType: 1, entityId: ent } });
check('create no users -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Mentions/${mid}/read`, { token: S });
check('non-recipient cannot mark mention read -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Mentions/${mid}/read`, { token: H });
check('recipient marks mention read -> 200', r.status === 200, brief(r));

{
  r = await call('POST', '/api/Task', { token: S, body: { title: 'mention-target-' + run, priority: 'Low' } });
  const target = r.json || {};
  r = await call('POST', '/api/Mentions/create', { token: S, body: { mentionedUserIds: [U3], entityType: 'Task', entityId: target.id } });
  check('mention on a real task returns the task title', r.status === 200 && r.json?.mentions?.[0]?.entityName === target.title, brief(r));
  r = await call('GET', '/api/Mentions', { token: H });
  const m = (r.json?.mentions || []).find(x => x.entityId === target.id);
  check('mention list says where you were mentioned', m?.entityName === target.title && m?.entityType === 'Task' && !!m?.mentionedByUserName, JSON.stringify(m || {}).slice(0, 220));
  n = (await notifs(H)).find(x => x.linkedEntityId === target.id && x.type === 'MentionedInTask');
  check('the mention notification names the task too', n?.linkedEntityName === target.title, JSON.stringify(n || {}).slice(0, 220));
  r = await call('PUT', '/api/Mentions/read-all', { token: H });
  check('mark all mentions read -> 200 with a count', r.status === 200 && r.json?.updated >= 1, brief(r));
  r = await call('GET', '/api/Mentions/unread-count', { token: H });
  check('no unread mentions afterwards', r.json?.unreadCount === 0, brief(r));
  r = await call('PUT', '/api/Mentions/read-all', { token: H });
  check('mark all read again -> 200, nothing to do', r.status === 200 && r.json?.updated === 0, brief(r));
}

// ───────────────────────── REMINDERS ─────────────────────────
console.log('\n== Due-soon reminders ==');
{
  r = await call('POST', '/api/Task', { token: S, body: { title: `soon-${run}`, priority: 'High', dueDate: new Date(Date.now() + 3 * 3600e3).toISOString(), assignedToUserId: U3 } });
  const soonId = r.json?.id;
  r = await call('POST', '/api/Task', { token: S, body: { title: `later-${run}`, priority: 'Low', dueDate: new Date(Date.now() + 5 * 864e5).toISOString() } });
  const laterId = r.json?.id;
  let mine = null, theirs = null;
  for (let i = 0; i < 20 && !(mine && theirs); i++) {
    await sleep(2000);
    mine = (await notifs(S)).find(x => x.linkedEntityId === soonId && x.type === 'TaskDueSoon');
    theirs = (await notifs(H)).find(x => x.linkedEntityId === soonId && x.type === 'TaskDueSoon');
  }
  check('owner gets a TaskDueSoon reminder for a task due in 3 hours', !!mine, JSON.stringify(mine || {}).slice(0, 200));
  check('assignee gets the reminder too', !!theirs, JSON.stringify(theirs || {}).slice(0, 200));
  await sleep(12000); // at least one more reminder pass
  const all = (await notifs(S)).filter(x => x.linkedEntityId === soonId && x.type === 'TaskDueSoon');
  check('the reminder is sent once, not on every pass', all.length === 1, `count=${all.length}`);
  const far = (await notifs(S)).find(x => x.linkedEntityId === laterId && x.type === 'TaskDueSoon');
  check('no reminder for a task due in 5 days', !far, JSON.stringify(far || {}).slice(0, 200));
}

// ───────────────────────── NOTIFICATIONS ─────────────────────────
console.log('\n== Notifications ==');
r = await call('GET', '/api/Notification', { token: S });
check('GET notifications -> 200', r.status === 200 && Array.isArray(r.json), brief(r));
const list = r.json || [];
check('notifications sorted newest first', list.every((x, i) => i === 0 || new Date(list[i - 1].createdAt) >= new Date(x.createdAt)));
check('notifications include triggeredByUserName', list.some(x => x.triggeredByUserId && x.triggeredByUserName), JSON.stringify(list.find(x => x.triggeredByUserId) || {}).slice(0, 200));
r = await call('GET', '/api/Notification/paged?pageIndex=0&pageSize=3', { token: S });
check('GET paged -> 3 items + real total', r.status === 200 && r.json?.items?.length === 3 && r.json?.totalCount === list.length, brief(r));
r = await call('GET', '/api/Notification/paged?pageIndex=-5&pageSize=100000', { token: S });
check('paged with silly values -> 200 (clamped)', r.status === 200 && r.json?.pageSize <= 100, brief(r));
r = await call('GET', '/api/Notification/unread', { token: S });
const unread = r.json || [];
r = await call('GET', '/api/Notification/unread-count', { token: S });
check('unread-count matches the unread list', r.status === 200 && r.json?.unreadCount === unread.length && unread.length > 0, brief(r));
const n0 = unread[0];
r = await call('PUT', `/api/Notification/${n0.id}/read`, { token: H });
check('cannot mark someone else\'s notification read', denied(r), brief(r));
r = await call('DELETE', `/api/Notification/${n0.id}`, { token: H });
check('cannot delete someone else\'s notification', denied(r), brief(r));
r = await call('PUT', `/api/Notification/${n0.id}/read`, { token: S });
check('mark own notification read -> 200', r.status === 200, brief(r));
r = await call('GET', '/api/Notification/unread-count', { token: S });
check('unread-count went down by one', r.json?.unreadCount === unread.length - 1, brief(r));
r = await call('PUT', '/api/Notification/read-all', { token: S });
check('read-all -> 200', r.status === 200, brief(r));
r = await call('GET', '/api/Notification/unread-count', { token: S });
check('unread-count is 0 after read-all', r.json?.unreadCount === 0, brief(r));
r = await call('DELETE', `/api/Notification/${n0.id}`, { token: S });
check('delete own notification -> 204', r.status === 204, brief(r));
r = await call('DELETE', `/api/Notification/${n0.id}`, { token: S });
check('delete again -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Notification/${NOBODY}/read`, { token: S });
check('mark unknown notification -> 404', r.status === 404, brief(r));

// ───────────────────────── CALENDAR MODULES ─────────────────────────
// Sarah (S) owns things, Ahmed (H) is her teammate, T2 is a stranger to both.
const HOUR = 3600e3, DAY = 864e5;
const iso = ms => new Date(ms).toISOString();
const sameMoment = (a, b) => new Date(a).getTime() === new Date(b).getTime();
const fileForm = (content, type, name) => { const f = new FormData(); f.append('file', new Blob([content], { type }), name); return f; };
const feedOf = async (token, from, to, extra = '') =>
  (await call('GET', `/api/Calendar/feed?from=${iso(from)}&to=${iso(to)}${extra}`, { token })).json || [];
const waitForNotification = async (token, match, seconds = 40) => {
  for (let i = 0; i < seconds / 2; i++) {
    const found = (await notifs(token)).find(match);
    if (found) return found;
    await sleep(2000);
  }
  return null;
};

console.log('\n== Calendars and calendar settings ==');
r = await call('GET', '/api/Calendar', { token: S });
check('everyone has exactly one default calendar', r.status === 200 && r.json.filter(c => c.isDefault).length === 1, brief(r));
const defaultCalendar = (r.json || []).find(c => c.isDefault) || {};
r = await call('POST', '/api/Calendar', { token: S, body: { name: `cal-${run}`, color: '#FF5733' } });
check('create calendar -> 201, visible, not default', r.status === 201 && r.json?.isDefault === false && r.json?.isVisible === true && r.json?.eventCount === 0, brief(r));
const cal = r.json || {};
r = await call('POST', '/api/Calendar', { token: S, body: { color: 'red' } });
check('create calendar without a name / with a bad colour -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Calendar/${cal.id}`, { token: S, body: { color: '#00AA88' } });
check('update keeps what is not sent (name) and changes what is (colour)', r.status === 200 && r.json?.name === `cal-${run}` && r.json?.color === '#00AA88', brief(r));
r = await call('PUT', `/api/Calendar/${cal.id}`, { token: H, body: { name: 'mine now' } });
check('somebody else\'s calendar does not exist for you -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Calendar/${defaultCalendar.id}`, { token: S });
check('the default calendar cannot be deleted -> 400', r.status === 400, brief(r));

// The account registered earlier in this run has never saved any, on every run.
r = await call('GET', '/api/Calendar/settings', { token: T2 });
check('calendar settings have sensible defaults before anything is saved', r.status === 200 && r.json?.workDayStart === '09:00' && r.json?.workDayEnd === '17:00' && r.json?.workDays?.length === 5 && !!r.json?.timeZone, brief(r));
const newSettings = { weekStartsOn: 'Saturday', defaultView: 'Week', use24HourClock: true, workDayStart: '08:30', workDayEnd: '16:00', workDays: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday'], defaultEventMinutes: 45, defaultReminderMinutes: 20 };
r = await call('PUT', '/api/Calendar/settings', { token: H, body: newSettings });
check('save calendar settings -> 200', r.status === 200 && r.json?.weekStartsOn === 'Saturday' && r.json?.workDayStart === '08:30', brief(r));
r = await call('GET', '/api/Calendar/settings', { token: H });
check('saved settings are read back', r.json?.defaultView === 'Week' && r.json?.use24HourClock === true && r.json?.workDays?.includes('Sunday') && !r.json?.workDays?.includes('Friday') && r.json?.defaultReminderMinutes === 20, brief(r));
r = await call('PUT', '/api/Calendar/settings', { token: H, body: { ...newSettings, workDayStart: '18:00' } });
check('a working day that ends before it starts -> 400', r.status === 400, brief(r));
r = await call('PUT', '/api/Calendar/settings', { token: H, body: { ...newSettings, weekStartsOn: 'Someday' } });
check('an unknown week day -> 400', r.status === 400, brief(r));

console.log('\n== Events and invitations ==');
// A project makes Sarah and Ahmed teammates, so she may invite him whatever their friendship is.
r = await call('POST', '/api/Group', { token: S, body: { name: `evt-${run}`, color: '#336699' } });
const eventGroup = r.json || {};
await call('POST', `/api/Group/${eventGroup.id}/members/${U3}`, { token: S });

const t0 = Math.ceil(Date.now() / HOUR) * HOUR + 3 * HOUR;
const eventTitle = `Planning-${run} pangolin`;
r = await call('POST', '/api/Event', { token: S, body: { title: eventTitle, description: 'Agenda inside', location: 'Room 2', startsAt: iso(t0), endsAt: iso(t0 + HOUR), calendarId: cal.id, attendeeIds: [U3], reminderMinutes: [10] } });
check('create event -> 201 in the chosen calendar, with the invited person pending', r.status === 201 && r.json?.isOwner === true && r.json?.calendarId === cal.id && r.json?.attendees?.length === 1 && r.json?.attendees[0]?.status === 'Pending' && !!r.json?.attendees[0]?.fullName, brief(r));
const ev = r.json || {};
r = await call('POST', '/api/Event', { token: S, body: { title: 'backwards', startsAt: iso(t0 + HOUR), endsAt: iso(t0) } });
check('an event that ends before it starts -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Event', { token: H, body: { title: 'not my calendar', startsAt: iso(t0), endsAt: iso(t0 + HOUR), calendarId: cal.id } });
check('an event cannot be put into somebody else\'s calendar -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Event', { token: S, body: { title: 'stranger', startsAt: iso(t0), endsAt: iso(t0 + HOUR), attendeeIds: [NEW] } });
check('only friends and teammates can be invited -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Event', { token: T2, body: { title: 'not my project', startsAt: iso(t0), endsAt: iso(t0 + HOUR), groupId: eventGroup.id } });
check('an event cannot be filed under a project you are not on -> 403', r.status === 403, brief(r));

r = await call('GET', `/api/Event/${ev.id}`, { token: H });
check('the invited person can open the event and sees their answer', r.status === 200 && r.json?.isOwner === false && r.json?.myResponse === 'Pending' && r.json?.calendarName === null, brief(r));
r = await call('GET', `/api/Event/${ev.id}`, { token: T2 });
check('a stranger cannot open the event -> 403', r.status === 403, brief(r));
r = await call('GET', `/api/Event/${NOBODY}`, { token: S });
check('unknown event -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Event/${ev.id}`, { token: H, body: { title: 'hijacked', startsAt: iso(t0), endsAt: iso(t0 + HOUR) } });
check('an invited person cannot change the event -> 403', r.status === 403, brief(r));
r = await call('DELETE', `/api/Event/${ev.id}`, { token: H });
check('an invited person cannot delete the event -> 403', r.status === 403, brief(r));

n = await waitForNotification(H, x => x.type === 'EventInvitation' && x.linkedEntityId === ev.id, 6);
check('the invited person gets an EventInvitation that names the event', !!n && n.linkedEntityType === 'Event' && n.linkedEntityName === eventTitle, JSON.stringify(n || {}).slice(0, 240));
r = await call('GET', '/api/Event/invitations', { token: H });
check('the invitation is listed under "invitations"', r.status === 200 && r.json.some(e => e.id === ev.id), brief(r));

{
  let hub = null;
  try { hub = await connectHub(S); } catch { /* checked below */ }
  r = await call('PUT', `/api/Event/${ev.id}/respond`, { token: H, body: { status: 'Accepted' } });
  check('accept the invitation -> 200', r.status === 200 && r.json?.myResponse === 'Accepted', brief(r));
  await sleep(1200);
  check('the owner\'s open tab hears "eventChanged" live', !!hub && hub.events.some(e => e.target === 'eventChanged' && e.args[0]?.eventId === ev.id), JSON.stringify(hub?.events || []).slice(0, 240));
  hub?.ws.close();
}
n = await waitForNotification(S, x => x.type === 'EventResponse' && x.linkedEntityId === ev.id, 6);
check('the owner is told about the answer', !!n && /accepted/.test(n.message), JSON.stringify(n || {}).slice(0, 200));
r = await call('GET', '/api/Event/invitations', { token: H });
check('an answered invitation leaves the "invitations" list', r.status === 200 && !r.json.some(e => e.id === ev.id), brief(r));
r = await call('PUT', `/api/Event/${ev.id}/respond`, { token: T2, body: { status: 'Accepted' } });
check('answering an event you are not invited to -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/Event/${ev.id}/respond`, { token: H, body: { status: 'Pending' } });
check('"Pending" is not an answer -> 400', r.status === 400, brief(r));

console.log('\n== Calendar feed ==');
let feed = await feedOf(S, t0 - DAY, t0 + DAY);
let item = feed.find(i => i.id === ev.id);
check('the owner\'s feed shows the event with its calendar and colour', !!item && item.kind === 'Event' && item.isOwner === true && item.calendarId === cal.id && item.color === '#00AA88' && item.attendeeCount === 1 && sameMoment(item.start, t0), JSON.stringify(item || {}).slice(0, 260));
feed = await feedOf(H, t0 - DAY, t0 + DAY);
item = feed.find(i => i.id === ev.id);
check('the guest\'s feed shows it too, as an invitation they accepted', !!item && item.isOwner === false && item.myResponse === 'Accepted' && item.calendarId === null, JSON.stringify(item || {}).slice(0, 260));
feed = await feedOf(T2, t0 - DAY, t0 + DAY);
check('a stranger\'s feed does not show it', !feed.some(i => i.id === ev.id), '');
check('the feed is sorted by start time', feed.every((i, k) => k === 0 || new Date(feed[k - 1].start) <= new Date(i.start)), '');

await call('PUT', `/api/Calendar/${cal.id}`, { token: S, body: { isVisible: false } });
feed = await feedOf(S, t0 - DAY, t0 + DAY);
check('events of a hidden calendar leave the owner\'s feed', !feed.some(i => i.id === ev.id), '');
feed = await feedOf(S, t0 - DAY, t0 + DAY, `&calendarIds=${cal.id}`);
check('...but asking for that calendar by id still returns them, and no tasks', feed.some(i => i.id === ev.id) && feed.every(i => i.kind === 'Event'), JSON.stringify(feed.map(i => i.kind)).slice(0, 120));
feed = await feedOf(H, t0 - DAY, t0 + DAY);
check('hiding a calendar does not hide the event from the guests', feed.some(i => i.id === ev.id), '');
await call('PUT', `/api/Calendar/${cal.id}`, { token: S, body: { isVisible: true } });

r = await call('POST', '/api/Task', { token: S, body: { title: `feed-task-${run}`, priority: 'Low', dueDate: iso(t0 + 2 * HOUR) } });
const feedTaskId = r.json?.id;
feed = await feedOf(S, t0 - DAY, t0 + DAY);
item = feed.find(i => i.id === feedTaskId);
check('tasks with a due date are in the feed as well', !!item && item.kind === 'Task' && item.status === 'Pending' && sameMoment(item.end, t0 + 2 * HOUR), JSON.stringify(item || {}).slice(0, 200));
feed = await feedOf(S, t0 - DAY, t0 + DAY, '&includeTasks=false');
check('includeTasks=false leaves the tasks out', !feed.some(i => i.kind === 'Task') && feed.some(i => i.id === ev.id), '');
r = await call('GET', `/api/Calendar/feed?to=${iso(t0)}`, { token: S });
check('feed without "from" -> 400', r.status === 400, brief(r));
r = await call('GET', `/api/Calendar/feed?from=${iso(t0)}&to=${iso(t0 + 400 * DAY)}`, { token: S });
check('feed over more than a year -> 400', r.status === 400, brief(r));

r = await call('PUT', `/api/Event/${ev.id}`, { token: S, body: { title: eventTitle, location: 'Room 5', startsAt: iso(t0 + HOUR), endsAt: iso(t0 + 2 * HOUR), calendarId: cal.id } });
check('the owner moves the event -> 200, the guest list is kept', r.status === 200 && sameMoment(r.json?.startsAt, t0 + HOUR) && r.json?.attendees?.length === 1 && r.json?.location === 'Room 5', brief(r));
n = await waitForNotification(H, x => x.type === 'EventChanged' && x.linkedEntityId === ev.id, 6);
check('the guest is told the event changed', !!n, JSON.stringify(n || {}).slice(0, 200));
r = await call('GET', `/api/Search?query=pangolin`, { token: H });
check('search finds events too (also the ones you are invited to)', r.status === 200 && r.json?.events?.some(e => e.id === ev.id && e.isOwner === false), brief(r));
r = await call('GET', `/api/Search?query=pangolin`, { token: T2 });
check('search does not leak other people\'s events', r.status === 200 && (r.json?.events || []).length === 0, brief(r));

console.log('\n== Reminders ==');
r = await call('GET', `/api/Event/${ev.id}/reminders`, { token: S });
check('the owner has the reminder the event was created with', r.status === 200 && r.json.length === 1 && r.json[0].minutesBefore === 10 && sameMoment(r.json[0].remindAt, t0 + HOUR - 10 * 60e3), brief(r));
r = await call('GET', `/api/Event/${ev.id}/reminders`, { token: H });
check('accepting gave the guest their own default reminder (20 min, from their settings)', r.status === 200 && r.json.length === 1 && r.json[0].minutesBefore === 20, brief(r));
r = await call('POST', `/api/Event/${ev.id}/reminders`, { token: H, body: { minutesBefore: 30 } });
check('add a reminder -> 201 with the moment it fires', r.status === 201 && sameMoment(r.json?.remindAt, t0 + HOUR - 30 * 60e3) && r.json?.itemTitle === eventTitle, brief(r));
const reminder = r.json || {};
r = await call('POST', `/api/Event/${ev.id}/reminders`, { token: H, body: { minutesBefore: 30 } });
check('the same reminder twice is one reminder', (r.status === 201 || r.status === 200) && r.json?.id === reminder.id, brief(r));
r = await call('POST', `/api/Event/${ev.id}/reminders`, { token: H, body: { minutesBefore: -5 } });
check('a negative reminder -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Event/${ev.id}/reminders`, { token: T2, body: { minutesBefore: 5 } });
check('no reminders on events you cannot see -> 403', r.status === 403, brief(r));
r = await call('POST', `/api/Event/${NOBODY}/reminders`, { token: S, body: { minutesBefore: 5 } });
check('reminder on an unknown event -> 404', r.status === 404, brief(r));
r = await call('GET', '/api/Reminder', { token: H });
check('"my upcoming reminders" lists them soonest first', r.status === 200 && r.json.some(x => x.id === reminder.id) && r.json.every((x, k) => k === 0 || new Date(r.json[k - 1].remindAt) <= new Date(x.remindAt)), brief(r));
r = await call('POST', `/api/Reminder/${reminder.id}/snooze`, { token: H, body: { minutes: 0 } });
check('snooze for 0 minutes -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Reminder/${reminder.id}/snooze`, { token: S, body: { minutes: 5 } });
check('somebody else\'s reminder cannot be snoozed -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Reminder/${reminder.id}`, { token: S });
check('...or deleted -> 404', r.status === 404, brief(r));
r = await call('DELETE', `/api/Reminder/${reminder.id}`, { token: H });
check('delete my reminder -> 204', r.status === 204, brief(r));
r = await call('PUT', `/api/Event/${ev.id}/respond`, { token: H, body: { status: 'Declined' } });
r = await call('GET', `/api/Event/${ev.id}/reminders`, { token: H });
check('declining removes the guest\'s reminders', r.status === 200 && r.json.length === 0, brief(r));
feed = await feedOf(H, t0 - DAY, t0 + DAY);
check('a declined event leaves the guest\'s feed', !feed.some(i => i.id === ev.id), '');

{
  // Fires for real: an event that starts in 70 seconds with a reminder one minute before.
  const soon = Date.now() + 70e3;
  r = await call('POST', '/api/Event', { token: S, body: { title: `remind-${run}`, startsAt: iso(soon), endsAt: iso(soon + HOUR), reminderMinutes: [1] } });
  const soonEventId = r.json?.id;
  r = await call('POST', '/api/Task', { token: S, body: { title: `remind-task-${run}`, priority: 'Low', dueDate: iso(Date.now() + 80e3) } });
  const soonTaskId = r.json?.id;
  r = await call('POST', `/api/Task/${soonTaskId}/reminders`, { token: S, body: { minutesBefore: 1 } });
  check('add a reminder to a task -> 201', r.status === 201 && r.json?.itemKind === 'Task', brief(r));

  const eventReminder = await waitForNotification(S, x => x.type === 'Reminder' && x.linkedEntityId === soonEventId, 50);
  check('the event reminder arrives as a notification', !!eventReminder && /starts/.test(eventReminder.message) && eventReminder.linkedEntityType === 'Event', JSON.stringify(eventReminder || {}).slice(0, 220));
  const taskReminder = await waitForNotification(S, x => x.type === 'Reminder' && x.linkedEntityId === soonTaskId, 50);
  check('the task reminder arrives as a notification', !!taskReminder && /is due/.test(taskReminder.message) && taskReminder.linkedEntityType === 'TaskItem', JSON.stringify(taskReminder || {}).slice(0, 220));
  await sleep(12000); // at least one more pass of the reminder job
  const count = (await notifs(S)).filter(x => x.type === 'Reminder' && (x.linkedEntityId === soonEventId || x.linkedEntityId === soonTaskId)).length;
  check('each reminder is sent once, not on every pass', count === 2, `count=${count}`);
  r = await call('GET', `/api/Event/${soonEventId}/reminders`, { token: S });
  check('a reminder that has fired for a one-off event has nothing ahead', r.json?.[0]?.remindAt === null && !!r.json?.[0]?.lastSentAt, brief(r));
}

console.log('\n== Repeating events ==');
const w0 = t0 + 2 * DAY;
r = await call('POST', '/api/Event', { token: S, body: { title: `weekly-${run}`, startsAt: iso(w0), endsAt: iso(w0 + HOUR / 2), recurrence: 'Weekly', recurrenceEndDate: iso(w0 + 21 * DAY) } });
check('create a weekly event -> 201', r.status === 201 && r.json?.recurrence === 'Weekly' && r.json?.recurrenceInterval === 1, brief(r));
const weekly = r.json || {};
const datesOf = async () => (await feedOf(S, w0 - DAY, w0 + 29 * DAY)).filter(i => i.id === weekly.id);
let dates = await datesOf();
check('the feed unfolds it into its four dates', dates.length === 4 && dates.every((d, k) => sameMoment(d.start, w0 + k * 7 * DAY) && sameMoment(d.occurrenceStart, d.start) && d.isRecurring), JSON.stringify(dates.map(d => d.start)));
r = await call('GET', `/api/Event?from=${iso(w0 - DAY)}&to=${iso(w0 + 29 * DAY)}`, { token: S });
check('the event list shows the series once', r.status === 200 && r.json.filter(e => e.id === weekly.id).length === 1, brief(r));
r = await call('GET', `/api/Event?from=${iso(w0 + 30 * DAY)}&to=${iso(w0 + 40 * DAY)}`, { token: S });
check('...and not at all in a range after it ended', r.status === 200 && !r.json.some(e => e.id === weekly.id), brief(r));

r = await call('PUT', `/api/Event/${weekly.id}/occurrences`, { token: S, body: { occurrenceStart: iso(w0 + 7 * DAY), cancelled: true } });
check('skip one date -> 200', r.status === 200, brief(r));
dates = await datesOf();
check('the skipped date is gone, the others stay', dates.length === 3 && !dates.some(d => sameMoment(d.start, w0 + 7 * DAY)), JSON.stringify(dates.map(d => d.start)));
r = await call('PUT', `/api/Event/${weekly.id}/occurrences`, { token: S, body: { occurrenceStart: iso(w0 + 14 * DAY), startsAt: iso(w0 + 15 * DAY), endsAt: iso(w0 + 15 * DAY + HOUR), title: 'moved once' } });
check('move one date -> 200', r.status === 200, brief(r));
dates = await datesOf();
item = dates.find(d => sameMoment(d.occurrenceStart, w0 + 14 * DAY));
check('the moved date shows at its new time with its own title', !!item && sameMoment(item.start, w0 + 15 * DAY) && item.title === 'moved once' && dates.length === 3, JSON.stringify(item || {}).slice(0, 200));
r = await call('PUT', `/api/Event/${weekly.id}/occurrences`, { token: S, body: { occurrenceStart: iso(w0 + 8 * DAY), cancelled: true } });
check('a date that is not part of the series -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Event/${ev.id}/occurrences`, { token: S, body: { occurrenceStart: iso(t0 + HOUR), cancelled: true } });
check('single dates of an event that does not repeat -> 400', r.status === 400, brief(r));
r = await call('PUT', `/api/Event/${weekly.id}/occurrences`, { token: H, body: { occurrenceStart: iso(w0), cancelled: true } });
check('somebody else cannot skip a date -> 403', r.status === 403, brief(r));
r = await call('DELETE', `/api/Event/${weekly.id}/occurrences?occurrenceStart=${iso(w0 + 7 * DAY)}`, { token: S });
check('undo the skip -> 204', r.status === 204, brief(r));
dates = await datesOf();
check('the date is back', dates.length === 4, JSON.stringify(dates.map(d => d.start)));
r = await call('DELETE', `/api/Event/${weekly.id}/occurrences?occurrenceStart=${iso(w0 + 7 * DAY)}`, { token: S });
check('undoing twice -> 404', r.status === 404, brief(r));
r = await call('PUT', `/api/Event/${weekly.id}`, { token: S, body: { title: `weekly-${run}`, startsAt: iso(w0 + HOUR), endsAt: iso(w0 + 1.5 * HOUR), recurrence: 'Weekly', recurrenceEndDate: iso(w0 + 21 * DAY) } });
dates = await datesOf();
check('moving the whole series resets changes to single dates', r.status === 200 && dates.length === 4 && dates.every((d, k) => sameMoment(d.start, w0 + HOUR + k * 7 * DAY) && d.title === `weekly-${run}`), JSON.stringify(dates.map(d => d.start)));
r = await call('POST', '/api/Event', { token: S, body: { title: 'fortnight', startsAt: iso(w0), endsAt: iso(w0 + HOUR), recurrence: 'Weekly', recurrenceInterval: 2 } });
const fortnightId = r.json?.id;
dates = (await feedOf(S, w0 - DAY, w0 + 29 * DAY)).filter(i => i.id === fortnightId);
check('"every 2 weeks" skips the weeks in between', dates.length === 3 && sameMoment(dates[1].start, w0 + 14 * DAY), JSON.stringify(dates.map(d => d.start)));
r = await call('DELETE', `/api/Event/${fortnightId}`, { token: S });
check('delete event -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Event/${fortnightId}`, { token: S });
check('a deleted event is gone -> 404', r.status === 404, brief(r));

console.log('\n== Calendar files (.ics) ==');
r = await call('GET', '/api/Calendar/export', { token: S });
check('export -> an .ics file with the events and the repeat rule', r.status === 200 && r.text.startsWith('BEGIN:VCALENDAR') && r.text.includes(`SUMMARY:weekly-${run}`) && r.text.includes('RRULE:FREQ=WEEKLY'), r.text.slice(0, 120));
const exported = r.text;
const incoming = ['BEGIN:VCALENDAR', 'VERSION:2.0',
  'BEGIN:VEVENT', `UID:one-${run}@test`, `SUMMARY:Imported one ${run}`, 'DTSTART:20270301T090000Z', 'DTEND:20270301T100000Z', 'END:VEVENT',
  'BEGIN:VEVENT', `UID:two-${run}@test`, `SUMMARY:Imported two ${run}`, 'DTSTART;VALUE=DATE:20270305', 'RRULE:FREQ=YEARLY', 'END:VEVENT',
  'BEGIN:VEVENT', `UID:bad-${run}@test`, 'SUMMARY:No start date', 'END:VEVENT',
  'END:VCALENDAR', ''].join('\r\n');
r = await call('POST', `/api/Calendar/import?calendarId=${cal.id}`, { token: S, form: fileForm(incoming, 'text/calendar', 'in.ics') });
check('import -> 2 events added, 1 entry that could not be read is counted', r.status === 200 && r.json?.imported === 2 && r.json?.updated === 0 && r.json?.skipped === 1 && r.json?.calendarId === cal.id, brief(r));
r = await call('POST', `/api/Calendar/import?calendarId=${cal.id}`, { token: S, form: fileForm(incoming, 'text/calendar', 'in.ics') });
check('importing the same file again updates instead of copying', r.status === 200 && r.json?.imported === 0 && r.json?.updated === 2, brief(r));
feed = await feedOf(S, Date.parse('2027-02-28T00:00:00Z'), Date.parse('2027-03-10T00:00:00Z'));
item = feed.find(i => i.title === `Imported two ${run}`);
check('imported events are on the calendar (the all-day yearly one too)', feed.some(i => i.title === `Imported one ${run}` && sameMoment(i.start, '2027-03-01T09:00:00Z')) && !!item && item.isAllDay && item.isRecurring, JSON.stringify(feed.map(i => i.title)).slice(0, 200));
r = await call('POST', '/api/Calendar/import', { token: S, form: fileForm(exported, 'text/calendar', 'own.ics') });
check('reading back Miqat\'s own export adds nothing new', r.status === 200 && r.json?.imported === 0 && r.json?.updated > 0, brief(r));
r = await call('POST', '/api/Calendar/import', { token: S, form: fileForm('hello', 'text/plain', 'notes.txt') });
check('a file that is not a calendar -> 400', r.status === 400, brief(r));
r = await call('POST', `/api/Calendar/import?calendarId=${cal.id}`, { token: H, form: fileForm(incoming, 'text/calendar', 'in.ics') });
check('importing into somebody else\'s calendar -> 400', r.status === 400, brief(r));

r = await call('DELETE', `/api/Calendar/${cal.id}`, { token: S });
check('delete a calendar -> 204', r.status === 204, brief(r));
r = await call('GET', `/api/Event/${ev.id}`, { token: S });
check('its events are kept and moved to the default calendar', r.status === 200 && r.json?.calendarId === defaultCalendar.id, brief(r));

console.log('\n== Free time and suggested times ==');
await call('PUT', `/api/Event/${ev.id}/respond`, { token: H, body: { status: 'Accepted' } });
const evStart = t0 + HOUR, evEnd = t0 + 2 * HOUR;
r = await call('GET', `/api/Schedule/availability?userIds=${U2}&userIds=${U3}&from=${iso(t0 - DAY)}&to=${iso(t0 + DAY)}`, { token: S });
const busyOf = id => (r.json || []).find(p => p.userId === id)?.busy || [];
check('availability -> busy times for both people', r.status === 200 && r.json.length === 2 && [U2, U3].every(id => busyOf(id).some(b => new Date(b.start) <= evStart && new Date(b.end) >= evEnd)), brief(r));
check('...and only times, never what the events are', (r.json || []).every(p => Object.keys(p).sort().join() === 'busy,fullName,timeZone,userId' && p.busy.every(b => Object.keys(b).sort().join() === 'end,start')), JSON.stringify(r.json?.[0] || {}).slice(0, 200));
r = await call('GET', `/api/Schedule/availability?userIds=${NEW}&from=${iso(t0)}&to=${iso(t0 + DAY)}`, { token: S });
check('the free time of a stranger is not shown -> 403', r.status === 403, brief(r));
r = await call('GET', `/api/Schedule/availability?from=${iso(t0)}&to=${iso(t0 + 40 * DAY)}`, { token: S });
check('availability over more than 31 days -> 400', r.status === 400, brief(r));
r = await call('POST', '/api/Schedule/suggest', { token: S, body: { attendeeIds: [U3], durationMinutes: 60, from: iso(evStart - HOUR), to: iso(evEnd + 6 * HOUR), maxResults: 4, workingHoursOnly: false } });
check('suggested times avoid what both people already have', r.status === 200 && r.json.length > 0 && r.json.every(s => new Date(s.end) <= evStart || new Date(s.start) >= evEnd) && r.json.every(s => new Date(s.end) - new Date(s.start) === HOUR), brief(r));
check('...start on the hour or half hour and never overlap each other', (r.json || []).every((s, k) => new Date(s.start).getTime() % (HOUR / 2) === 0 && (k === 0 || new Date(r.json[k - 1].end) <= new Date(s.start))), brief(r));
{
  // Sarah is in New York and works the default 09:00-17:00, Monday to Friday.
  const inNewYork = ms => Object.fromEntries(new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', weekday: 'short', hour: 'numeric', minute: 'numeric', hourCycle: 'h23' }).formatToParts(new Date(ms)).map(p => [p.type, p.value]));
  const minutesOf = ms => { const p = inNewYork(ms); return Number(p.hour) * 60 + Number(p.minute); };
  r = await call('POST', '/api/Schedule/suggest', { token: S, body: { attendeeIds: [], durationMinutes: 90, maxResults: 6 } });
  check('with working hours on, every suggestion lies inside her working day on her own clock', r.status === 200 && r.json.length === 6 && r.json.every(s => !['Sat', 'Sun'].includes(inNewYork(s.start).weekday) && minutesOf(s.start) >= 9 * 60 && minutesOf(s.start) + 90 <= 17 * 60), JSON.stringify((r.json || []).map(s => s.start)));
  const perDay = {};
  for (const s of r.json || []) { const p = inNewYork(s.start); perDay[new Date(s.start).toISOString().slice(0, 10) + p.weekday] = (perDay[new Date(s.start).toISOString().slice(0, 10) + p.weekday] || 0) + 1; }
  check('...and the list spreads over several days (3 a day at most)', Object.values(perDay).every(c => c <= 3) && Object.keys(perDay).length >= 2, JSON.stringify(perDay));
  // Ahmed is in Riyadh and saved 08:30-16:00, Sunday to Thursday: that never overlaps with her day.
  r = await call('POST', '/api/Schedule/suggest', { token: S, body: { attendeeIds: [U3], durationMinutes: 60, maxResults: 5 } });
  check('two people whose working hours never meet get no suggestion rather than a wrong one', r.status === 200 && r.json.length === 0, brief(r));
}
r = await call('POST', '/api/Schedule/suggest', { token: S, body: { attendeeIds: [NEW], durationMinutes: 60 } });
check('no suggestions with a stranger -> 403', r.status === 403, brief(r));
r = await call('POST', '/api/Schedule/suggest', { token: S, body: { attendeeIds: [], durationMinutes: 1 } });
check('a one-minute meeting -> 400', r.status === 400, brief(r));

console.log('\n== Checklists and dependencies ==');
const newTask = async title => (await call('POST', '/api/Task', { token: S, body: { title: `${title}-${run}`, priority: 'Medium' } })).json?.id;
const [taskA, taskB, taskC] = [await newTask('a'), await newTask('b'), await newTask('c')];
const addLine = (title, extra = {}) => call('POST', `/api/Task/${taskA}/subtasks`, { token: S, body: { title, ...extra } });
r = await addLine('one');
check('add a checklist line -> 201', r.status === 201 && r.json?.position === 0 && r.json?.isDone === false, brief(r));
const lineOne = r.json || {};
const lineTwo = (await addLine('two')).json || {};
const lineZero = (await addLine('zero', { position: 0 })).json || {};
const lines = async () => (await call('GET', `/api/Task/${taskA}/subtasks`, { token: S })).json || [];
let list2 = await lines();
check('a line can be put at the top; the others move down', list2.map(l => l.title).join() === 'zero,one,two' && list2.map(l => l.position).join() === '0,1,2', JSON.stringify(list2.map(l => [l.title, l.position])));
r = await call('PUT', `/api/Task/subtasks/${lineOne.id}`, { token: S, body: { isDone: true } });
check('tick a line -> 200 with the moment it was done, title kept', r.status === 200 && r.json?.isDone === true && !!r.json?.completedAt && r.json?.title === 'one', brief(r));
r = await call('GET', `/api/Task/${taskA}`, { token: S });
check('the task shows its progress (1 of 3)', r.json?.subtaskCount === 3 && r.json?.completedSubtaskCount === 1, brief(r));
r = await call('PUT', `/api/Task/subtasks/${lineTwo.id}`, { token: S, body: { position: 0 } });
list2 = await lines();
check('move a line to the top', r.status === 200 && list2.map(l => l.title).join() === 'two,zero,one', JSON.stringify(list2.map(l => l.title)));
r = await call('DELETE', `/api/Task/subtasks/${lineZero.id}`, { token: S });
list2 = await lines();
check('delete a line -> 204, the rest is renumbered', r.status === 204 && list2.map(l => l.position).join() === '0,1', JSON.stringify(list2.map(l => [l.title, l.position])));
r = await addLine('   ');
check('an empty line -> 400', r.status === 400, brief(r));
r = await call('GET', `/api/Task/${taskA}/subtasks`, { token: T2 });
check('no checklist of a task you cannot see -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/Task/subtasks/${lineOne.id}`, { token: T2, body: { isDone: false } });
check('...and no ticking its lines -> 403', r.status === 403, brief(r));
r = await call('PUT', `/api/Task/subtasks/${NOBODY}`, { token: S, body: { isDone: true } });
check('unknown checklist line -> 404', r.status === 404, brief(r));

const depend = (task, on, token = S) => call('POST', `/api/Task/${task}/dependencies`, { token, body: { dependsOnTaskId: on } });
r = await depend(taskA, taskB);
check('"a waits for b" -> 200, a is blocked', r.status === 200 && r.json?.isBlocked === true && r.json?.blockedBy?.length === 1 && r.json.blockedBy[0].id === taskB, brief(r));
r = await call('GET', `/api/Task/${taskA}`, { token: S });
check('the task itself says it is blocked', r.json?.isBlocked === true, brief(r));
r = await call('GET', `/api/Task/${taskB}/dependencies`, { token: S });
check('b lists what waits for it', r.status === 200 && r.json?.blocks?.some(t => t.id === taskA) && r.json?.isBlocked === false, brief(r));
r = await depend(taskB, taskA);
check('"b waits for a" would be a circle -> 400', r.status === 400, brief(r));
await depend(taskB, taskC);
r = await depend(taskC, taskA);
check('a longer circle (a > b > c > a) is refused too -> 400', r.status === 400, brief(r));
r = await depend(taskA, taskA);
check('a task cannot wait for itself -> 400', r.status === 400, brief(r));
r = await depend(taskA, taskC, T2);
check('a stranger cannot link the tasks -> 403', r.status === 403, brief(r));
r = await depend(taskA, NOBODY);
check('waiting for an unknown task -> 404', r.status === 404, brief(r));
await call('PUT', `/api/Task/${taskB}`, { token: S, body: { title: `b-${run}`, priority: 'Medium', status: 'Completed' } });
r = await call('GET', `/api/Task/${taskA}`, { token: S });
check('finishing b unblocks a', r.json?.isBlocked === false, brief(r));
r = await call('DELETE', `/api/Task/${taskA}/dependencies/${taskB}`, { token: S });
check('remove the link -> 204', r.status === 204, brief(r));
r = await call('DELETE', `/api/Task/${taskA}/dependencies/${taskB}`, { token: S });
check('removing it twice -> 404', r.status === 404, brief(r));

console.log('\n== Repeating tasks ==');
{
  const due = Math.ceil(Date.now() / HOUR) * HOUR + DAY;
  const title = `weekly-task-${run}`;
  const body = { title, priority: 'Medium', dueDate: iso(due), recurrence: 'Weekly' };
  r = await call('POST', '/api/Task', { token: S, body });
  const firstId = r.json?.id;
  await call('POST', `/api/Task/${firstId}/subtasks`, { token: S, body: { title: 'every week', isDone: true } });
  await call('POST', `/api/Task/${firstId}/reminders`, { token: S, body: { minutesBefore: 60 } });

  feed = (await feedOf(S, due - DAY, due + 15 * DAY)).filter(i => i.id === firstId);
  check('an open repeating task shows its coming dates in the feed', feed.length === 3 && feed.every((d, k) => sameMoment(d.end, due + k * 7 * DAY)), JSON.stringify(feed.map(d => d.end)));

  r = await call('PUT', `/api/Task/${firstId}`, { token: S, body: { ...body, status: 'Completed' } });
  const mine = async () => ((await call('GET', '/api/Task', { token: S })).json || []).filter(t => t.title === title);
  let all = await mine();
  const next = all.find(t => t.id !== firstId);
  check('finishing it creates the next one, a week later, still repeating', r.status === 204 && all.length === 2 && !!next && next.status === 'Pending' && sameMoment(next.dueDate, due + 7 * DAY) && next.recurrence === 'Weekly', JSON.stringify(all.map(t => [t.status, t.dueDate])));
  check('...with the checklist carried over, unticked', next?.subtaskCount === 1 && next?.completedSubtaskCount === 0, JSON.stringify(next || {}).slice(0, 200));
  r = await call('GET', `/api/Task/${next?.id}/reminders`, { token: S });
  check('...and the reminder too, pointed at the new due date', r.status === 200 && r.json.length === 1 && sameMoment(r.json[0].remindAt, due + 7 * DAY - HOUR), brief(r));
  r = await call('GET', `/api/Task/${firstId}/reminders`, { token: S });
  check('the finished task\'s reminder is switched off', r.json?.[0]?.remindAt === null, brief(r));

  await call('PUT', `/api/Task/${firstId}`, { token: S, body: { ...body, status: 'Pending' } });
  await call('PUT', `/api/Task/${firstId}`, { token: S, body: { ...body, status: 'Completed' } });
  all = await mine();
  check('reopening and finishing again does not create it twice', all.length === 2, `count=${all.length}`);
  feed = (await feedOf(S, due - DAY, due + 15 * DAY)).filter(i => i.kind === 'Task' && i.title === title);
  check('the feed shows the finished one once and the next one with its coming dates', feed.filter(i => i.id === firstId).length === 1 && feed.filter(i => i.id === next?.id).length === 2, JSON.stringify(feed.map(d => [d.status, d.end])));
}

console.log('\n== Attachments ==');
{
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64');
  r = await call('POST', `/api/Task/${taskA}/attachments`, { token: S, form: fileForm(png, 'image/png', 'pixel.png') });
  check('attach a file to a task -> 201', r.status === 201 && r.json?.fileName === 'pixel.png' && r.json?.contentType === 'image/png' && r.json?.sizeBytes === png.length && !!r.json?.uploadedByName, brief(r));
  const att = r.json || {};
  r = await call('GET', `/api/Task/${taskA}/attachments`, { token: S });
  check('list attachments -> 200', r.status === 200 && r.json.length === 1 && r.json[0].downloadUrl === `/api/Attachment/${att.id}`, brief(r));
  let res = await fetch(BASE + att.downloadUrl, { headers: { Authorization: `Bearer ${S}` } });
  const bytes = Buffer.from(await res.arrayBuffer());
  check('download returns the same bytes, as a download', res.status === 200 && bytes.equals(png) && /attachment/.test(res.headers.get('content-disposition') || '') && res.headers.get('x-content-type-options') === 'nosniff', `${res.status} ${res.headers.get('content-disposition')}`);
  res = await fetch(BASE + att.downloadUrl);
  check('download without signing in -> 401', res.status === 401, String(res.status));
  r = await call('GET', att.downloadUrl, { token: T2 });
  check('a stranger cannot download it -> 403', r.status === 403, brief(r));
  r = await call('POST', `/api/Task/${taskA}/attachments`, { token: T2, form: fileForm(png, 'image/png', 'x.png') });
  check('...or attach anything -> 403', r.status === 403, brief(r));
  r = await call('POST', `/api/Task/${taskA}/attachments`, { token: S, form: fileForm('MZ...', 'application/octet-stream', 'setup.exe') });
  check('a program cannot be attached -> 400', r.status === 400, brief(r));
  r = await call('POST', `/api/Task/${taskA}/attachments`, { token: S, form: fileForm('<script>alert(1)</script>', 'text/html', 'page.html') });
  check('...nor a web page -> 400', r.status === 400, brief(r));
  r = await call('POST', `/api/Event/${ev.id}/attachments`, { token: H, form: fileForm(png, 'image/png', 'guest.png') });
  check('a guest cannot attach to the event -> 403', r.status === 403, brief(r));
  r = await call('POST', `/api/Event/${ev.id}/attachments`, { token: S, form: fileForm('agenda', 'text/plain', '../../agenda.txt') });
  check('the owner can; a path in the file name is cut off', r.status === 201 && r.json?.fileName === 'agenda.txt' && r.json?.itemKind === 'Event', brief(r));
  const eventAtt = r.json || {};
  r = await call('GET', eventAtt.downloadUrl, { token: H });
  check('the guest can download what is attached to the event', r.status === 200 && r.text === 'agenda', brief(r));
  r = await call('DELETE', `/api/Attachment/${eventAtt.id}`, { token: H });
  check('...but not remove it -> 403', r.status === 403, brief(r));
  r = await call('DELETE', `/api/Attachment/${att.id}`, { token: S });
  check('remove an attachment -> 204', r.status === 204, brief(r));
  r = await call('GET', att.downloadUrl, { token: S });
  check('a removed attachment is gone -> 404', r.status === 404, brief(r));
}

console.log('\n== Devices ==');
{
  const subscription = { endpoint: `https://fcm.googleapis.com/fcm/send/e2e-${run}`, p256dh: 'BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM', auth: 'tBHItJI5svbpez7KI4CCXg', name: 'Test browser' };
  r = await call('GET', '/api/Device/push-key', { token: S });
  check('push key -> 200 and says whether the server can send', r.status === 200 && typeof r.json?.isConfigured === 'boolean', brief(r));
  r = await call('POST', '/api/Device', { token: S, body: subscription });
  check('register a browser -> 200', r.status === 200 && r.json?.name === 'Test browser', brief(r));
  const deviceId = r.json?.id;
  r = await call('POST', '/api/Device', { token: S, body: subscription });
  check('registering it again updates it, no second row', r.status === 200 && r.json?.id === deviceId && (await call('GET', '/api/Device', { token: S })).json.length === 1, brief(r));
  r = await call('POST', '/api/Device', { token: S, body: { ...subscription, endpoint: 'https://attacker.example.com/push' } });
  check('an address that is not a browser push service is refused -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Device', { token: S, body: { ...subscription, endpoint: 'http://fcm.googleapis.com/fcm/send/plain' } });
  check('...and so is plain http -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Device', { token: S, body: { ...subscription, endpoint: 'https://fcm.googleapis.com.attacker.example/push' } });
  check('...and a look-alike host -> 400', r.status === 400, brief(r));
  r = await call('DELETE', `/api/Device/${deviceId}`, { token: H });
  check('somebody else\'s device -> 404', r.status === 404, brief(r));
  r = await call('POST', '/api/Device', { token: H, body: subscription });
  check('the same browser, another person signed in: it moves to them', r.status === 200 && (await call('GET', '/api/Device', { token: S })).json.length === 0, brief(r));
  r = await call('DELETE', `/api/Device/${r.json?.id}`, { token: H });
  check('remove a device -> 204', r.status === 204, brief(r));
}

console.log('\n== Change email ==');
{
  const oldEmail = `ce.${run}@example.com`, nextEmail = `ce.new.${run}@example.com`, password = 'Passw0rd!x';
  await call('POST', '/api/Auth/register', { body: { fullName: 'Change Email', email: oldEmail, password, timeZone: 'UTC' } });
  r = await call('POST', '/api/Auth/verify-otp', { body: { email: oldEmail, code: otpFromLogs(oldEmail, 'EmailVerification'), purpose: 'EmailVerification' } });
  const token = r.json?.accessToken;
  r = await call('POST', '/api/Auth/change-email', { token, body: { newEmail: nextEmail, currentPassword: 'Wrong@1234' } });
  check('change email with the wrong password -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Auth/change-email', { token, body: { newEmail: 'sarah@miqat.app', currentPassword: password } });
  check('change email to an address that is taken -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Auth/change-email', { token, body: { newEmail: 'not-an-email', currentPassword: password } });
  check('change email to something that is not an address -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Auth/change-email', { body: { newEmail: nextEmail, currentPassword: password } });
  check('change email without signing in -> 401', r.status === 401, brief(r));
  r = await call('POST', '/api/Auth/change-email', { token, body: { newEmail: nextEmail.toUpperCase(), currentPassword: password } });
  check('ask for the change -> 200, the code goes to the NEW address', r.status === 200 && !!otpFromLogs(nextEmail, 'EmailChange') && !otpFromLogs(oldEmail, 'EmailChange'), brief(r));
  r = await call('POST', '/api/Auth/login', { body: { email: oldEmail, password } });
  check('nothing changes until the code is confirmed', r.status === 200 && r.json?.email?.toLowerCase() === oldEmail, brief(r));
  r = await call('POST', '/api/Auth/change-email/confirm', { token, body: { code: '000000' } });
  check('confirm with a wrong code -> 400', r.status === 400, brief(r));
  r = await call('POST', '/api/Auth/change-email/confirm', { token, body: { code: otpFromLogs(nextEmail, 'EmailChange') } });
  check('confirm with the right code -> 200 and the new address', r.status === 200 && r.json?.email === nextEmail, brief(r));
  r = await call('POST', '/api/Auth/login', { body: { email: oldEmail, password } });
  check('the old address no longer signs in', r.status === 401, brief(r));
  r = await call('POST', '/api/Auth/login', { body: { email: nextEmail, password } });
  check('the new address does', r.status === 200, brief(r));
  r = await call('POST', '/api/Auth/change-email/confirm', { token, body: { code: otpFromLogs(nextEmail, 'EmailChange') } });
  check('the code cannot be used twice -> 400', r.status === 400, brief(r));
}

// ───────────────────────── CHAT ─────────────────────────
console.log('\n== Chat ==');
{
  // Sarah (U2) and Ahmed (U3) are friends in the seed data; T is a stranger to both.
  const invoke = (hub, target, ...args) => hub.ws.send(JSON.stringify({ type: 1, target, arguments: args }) + '\x1e');
  const seen = (hub, target, test) => hub.events.some(e => e.target === target && test(e.args[0] || {}));

  r = await call('GET', '/api/Chat', { token: S });
  check('GET my chats -> 200 array', r.status === 200 && Array.isArray(r.json), brief(r));

  r = await call('POST', `/api/Chat/with/${U3}`, { token: S });
  check('open the chat with a friend -> 200, names the friend', r.status === 200 && r.json?.friend?.id === U3 && r.json?.canSend === true && !!r.json?.id, brief(r));
  const chatId = r.json?.id;
  r = await call('POST', `/api/Chat/with/${U3}`, { token: S });
  check('opening it again gives the same chat', r.json?.id === chatId, brief(r));
  r = await call('POST', `/api/Chat/with/${U2}`, { token: H });
  check('the friend opening it gets the same chat too', r.json?.id === chatId && r.json?.friend?.id === U2, brief(r));
  r = await call('POST', `/api/Chat/with/${U2}`, { token: S });
  check('a chat with yourself -> 400', r.status === 400, brief(r));
  r = await call('POST', `/api/Chat/with/${NOBODY}`, { token: S });
  check('a chat with someone who is not a friend -> 403', r.status === 403, brief(r));
  r = await call('POST', `/api/Chat/with/${U2}`, { token: T });
  check('a stranger cannot open a chat -> 403', r.status === 403, brief(r));

  let hubS = null, hubH = null, hubT = null;
  try { hubS = await connectHub(S); hubH = await connectHub(H); hubT = await connectHub(T); } catch (e) { check('chat: hubs connect', false, String(e)); }

  if (hubS && hubH && hubT) {
    await sleep(500);
    r = await call('GET', '/api/Chat/online', { token: H });
    check('Ahmed sees Sarah online while her app is open', r.status === 200 && r.json?.includes(U2), brief(r));

    r = await call('POST', `/api/Chat/${chatId}/messages`, { token: S, body: { body: `  hello from sarah ${run}  `, clientId: 'local-1' } });
    check('send -> 201, text trimmed, clientId echoed', r.status === 201 && r.json?.body === `hello from sarah ${run}` && r.json?.clientId === 'local-1' && r.json?.senderId === U2, brief(r));
    const first = r.json;
    await sleep(1200);
    check('the friend receives "chatMessage" live', seen(hubH, 'chatMessage', m => m.id === first?.id && m.clientId === 'local-1'), JSON.stringify(hubH.events).slice(0, 300));
    check('the sender\'s other tabs receive it too', seen(hubS, 'chatMessage', m => m.id === first?.id), '');
    check('a stranger receives nothing', !seen(hubT, 'chatMessage', m => m.id === first?.id), '');

    r = await call('GET', '/api/Chat/unread-count', { token: H });
    check('the friend has it as unread', r.status === 200 && r.json?.messages >= 1 && r.json?.conversations >= 1, brief(r));
    r = await call('GET', '/api/Chat', { token: H });
    let row = (r.json || []).find(c => c.id === chatId);
    check('chat list: newest message, unread count, friend online', row?.lastMessage?.id === first?.id && row?.unreadCount >= 1 && row?.friend?.id === U2 && row?.friend?.isOnline === true, JSON.stringify(row || {}).slice(0, 300));

    invoke(hubH, 'Typing', chatId);
    await sleep(1000);
    check('"is typing" reaches the other person', seen(hubS, 'chatTyping', t => t.conversationId === chatId && t.userId === U3), JSON.stringify(hubS.events.filter(e => e.target === 'chatTyping')).slice(0, 200));
    invoke(hubT, 'Typing', chatId);
    await sleep(800);
    check('a stranger\'s "is typing" goes nowhere', !seen(hubS, 'chatTyping', t => t.userId === NEW), '');

    r = await call('GET', `/api/Chat/${chatId}`, { token: T });
    check('a stranger cannot read the chat -> 403', r.status === 403, brief(r));
    r = await call('GET', `/api/Chat/${chatId}/messages`, { token: T });
    check('a stranger cannot read its messages -> 403', r.status === 403, brief(r));
    r = await call('POST', `/api/Chat/${chatId}/messages`, { token: T, body: { body: 'let me in' } });
    check('a stranger cannot write in it -> 403', r.status === 403, brief(r));
    r = await call('POST', `/api/Chat/${chatId}/messages`, { token: S, body: { body: '   ' } });
    check('an empty message -> 400', r.status === 400, brief(r));
    r = await call('POST', `/api/Chat/${chatId}/messages`, { token: S, body: { body: 'x'.repeat(4001) } });
    check('a message over 4000 characters -> 400', r.status === 400, brief(r));
    r = await call('POST', `/api/Chat/${chatId}/messages`, { token: S, body: { body: 'ok', clientId: 'c'.repeat(65) } });
    check('a clientId over 64 characters -> 400', r.status === 400, brief(r));
    r = await call('GET', `/api/Chat/${NOBODY}`, { token: S });
    check('an unknown chat -> 404', r.status === 404, brief(r));

    // Paging: newest page first, then the page before it, each oldest first.
    for (const text of ['two', 'three', 'four']) await call('POST', `/api/Chat/${chatId}/messages`, { token: H, body: { body: `${text} ${run}` } });
    r = await call('GET', `/api/Chat/${chatId}/messages?limit=2`, { token: S });
    const page = r.json?.messages || [];
    check('newest page: 2 messages, oldest first, more before', r.status === 200 && page.length === 2 && page[0].body === `three ${run}` && page[1].body === `four ${run}` && r.json?.hasMore === true, brief(r));
    r = await call('GET', `/api/Chat/${chatId}/messages?limit=2&before=${page[0]?.id}`, { token: S });
    const older = r.json?.messages || [];
    check('the page before it continues where it stopped', older.length === 2 && older[1].body === `two ${run}` && older[0].id === first?.id, brief(r));
    r = await call('GET', `/api/Chat/${chatId}/messages?before=${NOBODY}`, { token: S });
    check('"before" a message of another chat -> 400', r.status === 400, brief(r));

    r = await call('GET', `/api/Chat/${chatId}`, { token: H });
    check('replying counts as having read what came before', r.json?.unreadCount === 0, brief(r));
    r = await call('GET', `/api/Chat/${chatId}`, { token: S });
    check('the other side has the three replies unread (own messages never count)', r.json?.unreadCount === 3, brief(r));
    r = await call('PUT', `/api/Chat/${chatId}/read`, { token: S });
    check('mark as read -> 204', r.status === 204, brief(r));
    await sleep(1000);
    check('the friend sees "read" live', seen(hubH, 'chatRead', x => x.conversationId === chatId && x.userId === U2), JSON.stringify(hubH.events.filter(e => e.target === 'chatRead')).slice(0, 200));
    r = await call('GET', `/api/Chat/${chatId}`, { token: S });
    check('nothing unread after reading', r.json?.unreadCount === 0, brief(r));
    r = await call('GET', `/api/Chat/${chatId}`, { token: H });
    check('the friend knows how far the chat was read ("seen")', !!r.json?.friendLastReadAt && new Date(r.json.friendLastReadAt) >= new Date(r.json?.lastMessage?.createdAt), brief(r));

    r = await call('PUT', `/api/Chat/messages/${first?.id}`, { token: H, body: { body: 'not mine' } });
    check('editing someone else\'s message -> 403', r.status === 403, brief(r));
    r = await call('PUT', `/api/Chat/messages/${first?.id}`, { token: S, body: { body: 'hello, edited' } });
    check('edit own message -> 200, marked edited', r.status === 200 && r.json?.body === 'hello, edited' && !!r.json?.editedAt, brief(r));
    await sleep(1000);
    check('the friend sees the edit live', seen(hubH, 'chatMessageChanged', m => m.id === first?.id && m.body === 'hello, edited'), '');
    r = await call('DELETE', `/api/Chat/messages/${first?.id}`, { token: H });
    check('deleting someone else\'s message -> 403', r.status === 403, brief(r));
    r = await call('DELETE', `/api/Chat/messages/${first?.id}`, { token: S });
    check('delete own message -> 204', r.status === 204, brief(r));
    r = await call('DELETE', `/api/Chat/messages/${first?.id}`, { token: S });
    check('deleting it again -> 204', r.status === 204, brief(r));
    r = await call('PUT', `/api/Chat/messages/${first?.id}`, { token: S, body: { body: 'back' } });
    check('a deleted message cannot be edited -> 400', r.status === 400, brief(r));
    r = await call('GET', `/api/Chat/${chatId}/messages?limit=100`, { token: H });
    const gone = (r.json?.messages || []).find(m => m.id === first?.id);
    check('a deleted message keeps its place, without its text', gone?.isDeleted === true && gone?.body === '', JSON.stringify(gone || {}).slice(0, 200));
    r = await call('PUT', `/api/Chat/messages/${NOBODY}`, { token: S, body: { body: 'x' } });
    check('editing an unknown message -> 404', r.status === 404, brief(r));

    hubH.ws.close();
    await sleep(1500);
    check('Sarah sees Ahmed go offline when his app closes', seen(hubS, 'presenceChanged', p => p.userId === U3 && p.isOnline === false), JSON.stringify(hubS.events.filter(e => e.target === 'presenceChanged')).slice(0, 200));
    hubS.ws.close(); hubT.ws.close();
  }

  // After unfriending, the history stays readable but nothing new can be sent.
  r = await call('GET', '/api/Friends/pending', { token: S });
  const request = (r.json?.pendingRequests || []).find(p => p.senderId === NEW);
  r = await call('PUT', `/api/Friends/accept/${request?.id}`, { token: S });
  r = await call('POST', `/api/Chat/with/${U2}`, { token: T });
  const exChat = r.json?.id;
  r = await call('POST', `/api/Chat/${exChat}/messages`, { token: T, body: { body: 'hi new friend' } });
  check('a new friend can write', r.status === 201, brief(r));
  r = await call('DELETE', `/api/Friends/${request?.id}`, { token: T });
  r = await call('GET', `/api/Chat/${exChat}`, { token: S });
  check('after unfriending the chat is still readable, but closed', r.status === 200 && r.json?.canSend === false && r.json?.lastMessage?.body === 'hi new friend', brief(r));
  r = await call('POST', `/api/Chat/${exChat}/messages`, { token: S, body: { body: 'still there?' } });
  check('after unfriending nothing new can be sent -> 403', r.status === 403, brief(r));
}

// ───────────────────────── CALLS ─────────────────────────
console.log('\n== Calls ==');
{
  // Sarah (U2) calls Ahmed (U3); T is a stranger again by now. The sound and picture never
  // pass the server, so what is checked here is the ringing, the routing and the record.
  const got = (hub, target, test = () => true) => hub.events.some(e => e.target === target && test(e.args[0] || {}));
  const lastMessage = async token => ((await call('GET', `/api/Chat/${chatId}/messages?limit=1`, { token })).json?.messages || [])[0];

  r = await call('GET', '/api/Call/setup');
  check('call setup without a token -> 401', r.status === 401, brief(r));
  r = await call('GET', '/api/Call/setup', { token: S });
  check('call setup -> 200 with helper servers and the ring time', r.status === 200 && r.json?.ringSeconds > 0 && r.json?.iceServers?.some(s => s.urls?.some(u => u.startsWith('stun:'))), brief(r));

  r = await call('POST', `/api/Chat/with/${U3}`, { token: S });
  const chatId = r.json?.id;

  let hubS = await connectHub(S);
  // Ahmed's app is closed: nobody to ring.
  let done = await hubCall(hubS, 'StartCall', chatId, false);
  check('calling a friend who is offline fails with a message to show', /not online/i.test(done.error || ''), JSON.stringify(done));
  let logged = await lastMessage(H);
  check('...and leaves a missed call in the chat', logged?.callOutcome === 'Missed' && logged?.callVideo === false && logged?.senderId === U2 && logged?.body === '', JSON.stringify(logged || {}));
  r = await call('GET', `/api/Chat/${chatId}`, { token: H });
  check('a missed call counts as unread for the friend', r.json?.unreadCount >= 1, brief(r));
  r = await call('PUT', `/api/Chat/messages/${logged?.id}`, { token: S, body: { body: 'never happened' } });
  check('the record of a call cannot be edited -> 400', r.status === 400, brief(r));
  r = await call('DELETE', `/api/Chat/messages/${logged?.id}`, { token: S });
  check('the record of a call cannot be deleted -> 400', r.status === 400, brief(r));

  const hubH = await connectHub(H), hubH2 = await connectHub(H), hubT = await connectHub(T);
  await sleep(400);

  done = await hubCall(hubT, 'StartCall', chatId, true);
  check('a stranger cannot call into a chat', !!done.error, JSON.stringify(done));

  done = await hubCall(hubS, 'StartCall', chatId, true);
  const callId = done.result;
  check('starting a video call answers its id', typeof callId === 'string' && callId.length === 36, JSON.stringify(done));
  await sleep(700);
  const ringing = e => e.callId === callId && e.conversationId === chatId && e.video === true && e.from?.id === U2 && !!e.from?.fullName;
  check('it rings in every open tab of the friend, naming the caller', got(hubH, 'callIncoming', ringing) && got(hubH2, 'callIncoming', ringing), JSON.stringify(hubH.events.filter(e => e.target === 'callIncoming')).slice(0, 300));
  check('it rings for nobody else', !got(hubT, 'callIncoming') && !got(hubS, 'callIncoming'), '');
  done = await hubCall(hubS, 'StartCall', chatId, false);
  check('a second call while one is ringing is refused', /already on a call/i.test(done.error || ''), JSON.stringify(done));

  await hubCall(hubH, 'SendCallSignal', callId, { type: 'early' });
  done = await hubCall(hubT, 'AnswerCall', callId, true);
  check('a stranger cannot answer the call', !!done.error, JSON.stringify(done));
  done = await hubCall(hubH, 'AnswerCall', callId, true);
  await sleep(700);
  check('the friend picks up', !done.error && got(hubS, 'callAccepted', e => e.callId === callId), JSON.stringify(done));
  check('it stops ringing in the friend\'s other tab only', got(hubH2, 'callEnded', e => e.callId === callId && e.reason === 'answeredElsewhere') && !got(hubH, 'callEnded', e => e.callId === callId), '');
  done = await hubCall(hubH2, 'AnswerCall', callId, true);
  check('the call cannot be answered twice', !!done.error, JSON.stringify(done));

  await hubCall(hubS, 'SendCallSignal', callId, { type: 'offer', sdp: 'v=0 from-sarah' });
  await hubCall(hubH, 'SendCallSignal', callId, { type: 'answer', sdp: 'v=0 from-ahmed' });
  await hubCall(hubH2, 'SendCallSignal', callId, { type: 'intruder' });
  await hubCall(hubT, 'SendCallSignal', callId, { type: 'intruder' });
  await sleep(700);
  check('set-up messages reach the other tab of the call, unchanged', got(hubH, 'callSignal', e => e.callId === callId && e.data?.type === 'offer' && e.data?.sdp === 'v=0 from-sarah') && got(hubS, 'callSignal', e => e.data?.type === 'answer' && e.data?.sdp === 'v=0 from-ahmed'), JSON.stringify(hubH.events.filter(e => e.target === 'callSignal')).slice(0, 300));
  check('...and nobody else: not the other tab, not a stranger, nothing sent before the answer', !got(hubH2, 'callSignal') && !got(hubT, 'callSignal') && !got(hubS, 'callSignal', e => e.data?.type === 'intruder' || e.data?.type === 'early'), '');

  await hubCall(hubT, 'EndCall', callId);
  await sleep(1300);
  check('a stranger cannot hang up for them', !got(hubS, 'callEnded', e => e.callId === callId), '');
  await hubCall(hubH, 'EndCall', callId);
  await sleep(800);
  check('hanging up ends the call for both', got(hubS, 'callEnded', e => e.callId === callId && e.reason === 'ended') && got(hubH, 'callEnded', e => e.callId === callId && e.reason === 'ended'), '');
  logged = await lastMessage(S);
  check('the call is in the chat with how long it lasted', logged?.callOutcome === 'Completed' && logged?.callVideo === true && logged?.callSeconds >= 1 && logged?.senderId === U2, JSON.stringify(logged || {}));
  check('both see that record live', got(hubS, 'chatMessage', m => m.id === logged?.id && m.callOutcome === 'Completed') && got(hubH, 'chatMessage', m => m.id === logged?.id), '');
  r = await call('GET', `/api/Chat/${chatId}`, { token: H });
  check('a call that was taken leaves nothing unread', r.json?.unreadCount === 0, brief(r));

  done = await hubCall(hubS, 'StartCall', chatId, false);
  await hubCall(hubH2, 'AnswerCall', done.result, false);
  await sleep(700);
  check('declining tells the caller', got(hubS, 'callEnded', e => e.callId === done.result && e.reason === 'declined'), '');
  logged = await lastMessage(S);
  check('...and is recorded as declined', logged?.callOutcome === 'Declined' && logged?.callVideo === false, JSON.stringify(logged || {}));

  done = await hubCall(hubS, 'StartCall', chatId, false);
  await hubCall(hubS, 'EndCall', done.result);
  await sleep(700);
  check('the caller giving up stops the ringing', got(hubH, 'callEnded', e => e.callId === done.result && e.reason === 'cancelled'), '');
  logged = await lastMessage(H);
  check('...and is a missed call for the friend', logged?.callOutcome === 'Missed', JSON.stringify(logged || {}));

  done = await hubCall(hubS, 'StartCall', chatId, true);
  await hubCall(hubH, 'AnswerCall', done.result, true);
  await sleep(500);
  hubS.ws.close();
  await sleep(1500);
  check('closing the tab ends the call for the other side', got(hubH, 'callEnded', e => e.callId === done.result), '');
  logged = await lastMessage(H);
  check('...and it is recorded as a call that took place', logged?.callOutcome === 'Completed', JSON.stringify(logged || {}));

  // T and Sarah were friends for a moment in the chat section; that chat still exists.
  r = await call('GET', '/api/Chat', { token: T });
  const exChat = (r.json || []).find(c => c.friend?.id === U2)?.id;
  done = await hubCall(hubT, 'StartCall', exChat, false);
  check('someone who is no longer a friend cannot call', /friends/i.test(done.error || ''), JSON.stringify(done));

  hubH.ws.close(); hubH2.ws.close(); hubT.ws.close();
}

// ───────────────────────── RATE LIMITING ─────────────────────────
console.log('\n== Rate limiting ==');
{
  const victim = `ratelimit.${run}@example.com`;
  const statuses = [];
  for (let i = 0; i < 45; i++) statuses.push((await call('POST', '/api/Auth/login', { body: { email: victim, password: 'Wrong@1234' } })).status);
  check('repeated logins for one email are cut off with 429', statuses.slice(0, 40).every(s => s === 401) && statuses.slice(40).every(s => s === 429), statuses.join(','));
  r = await call('POST', '/api/Auth/login', { body: { email: `other.${run}@example.com`, password: 'Wrong@1234' } });
  check('a different email is not affected (budget is per email)', r.status === 401, brief(r));
  r = await call('POST', '/api/Auth/login', { body: { email: 'ahmed@miqat.app', password: 'Test@1234' } });
  check('real users can still sign in meanwhile', r.status === 200, brief(r));
}

// ───────────────────────── MISC ─────────────────────────
console.log('\n== Misc ==');
r = await call('GET', '/swagger/v1/swagger.json');
check('swagger json -> 200 (enabled in this environment)', r.status === 200, brief(r));
const paths = Object.keys(r.json?.paths || {});
console.log(`      swagger lists ${paths.length} paths`);
r = await call('GET', '/api/Task/not-a-guid', { token: S });
check('bad guid -> 400/404', r.status === 400 || r.status === 404, brief(r));
r = await call('POST', '/api/Task', { token: S, body: 'not json' });
check('malformed body -> 400', r.status === 400, brief(r));
r = await call('GET', '/health');
check('health endpoint -> 200', r.status === 200, brief(r));

r = await call('DELETE', `/api/User/${U1}`, { token: A });
check('admin cannot delete their own account -> 400', r.status === 400, brief(r));
// A request that is still open when its sender's account is deleted.
r = await call('POST', `/api/Friends/send-request/${U3}`, { token: T });
const orphanRequest = r.json?.friendship?.id;
r = await call('GET', '/api/Friends/pending', { token: H });
check('an open friend request is listed for its receiver', r.json?.pendingRequests?.some(p => p.id === orphanRequest), brief(r));

r = await call('DELETE', `/api/User/${NEW}`, { token: A });
check('admin deletes user -> 204', r.status === 204, brief(r));
r = await call('GET', '/api/Friends/pending', { token: H });
check('a deleted account\'s friend requests disappear from other people\'s lists', r.status === 200 && !r.json?.pendingRequests?.some(p => p.senderId === NEW), brief(r));
r = await call('PUT', `/api/Friends/accept/${orphanRequest}`, { token: H });
check('...and can no longer be accepted -> 404', r.status === 404, brief(r));
for (const [who, token] of [['Sarah', S], ['Ahmed', H]]) {
  const pending = (await call('GET', '/api/Friends/pending', { token })).json?.pendingRequests || [];
  const friendList = (await call('GET', '/api/Friends', { token })).json?.friends || [];
  const people = [...pending, ...friendList].map(p => p.friendId);
  const statuses = await Promise.all(people.map(async id => (await call('GET', `/api/User/${id}/profile`, { token })).status));
  check(`everyone in ${who}'s friends and requests lists has a profile that opens`, statuses.every(s => s === 200), `${statuses.filter(s => s !== 200).length} of ${people.length} do not`);
}
r = await call('POST', '/api/Auth/login', { body: { email, password: 'Another1!x' } });
check('deleted user cannot login', r.status === 401, brief(r));
const before = devOtpLines(email, 'PasswordReset').length;
r = await call('POST', '/api/Auth/forgot-password', { body: { email } });
check('deleted user gets no reset code (reply stays 200)', r.status === 200 && devOtpLines(email, 'PasswordReset').length === before, `status=${r.status}`);

console.log(`\n${pass} passed, ${fail} failed, ${pass + fail} total`);
if (fail) { console.log('\nFailures:'); results.filter(x => !x.ok).forEach(x => console.log(` - ${x.name}  -> ${x.detail}`)); }
process.exit(fail ? 1 : 0);
