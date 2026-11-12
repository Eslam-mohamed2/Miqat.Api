// Stand-ins for the two outside services notifications leave through, so the
// delivery check (delivery-e2e.mjs) can see what the API really sends:
//
//   port 2525  a mail server (SMTP): accepts every message and keeps it
//   port 2526  a browser push service: accepts every push and keeps it;
//              an address containing "/gone/" answers 410, like a browser
//              that unsubscribed
//   port 2527  what was captured, as JSON:  GET /mails   GET /pushes
//
// It runs inside the Docker network of the API (see delivery-e2e.mjs), because
// that is where the API can reach it.
import net from 'node:net';
import http from 'node:http';

const mails = [];
const pushes = [];

// ── Mail ────────────────────────────────────────────────────────────────────
net.createServer(socket => {
  let buffer = '';
  let inData = false;
  let mail = { from: null, to: [], data: '' };
  const say = line => socket.write(line + '\r\n');

  say('220 fake-smtp ready');
  socket.on('error', () => {});
  socket.on('data', chunk => {
    buffer += chunk.toString('utf8');

    while (true) {
      if (inData) {
        const end = buffer.indexOf('\r\n.\r\n');
        if (end < 0) return;
        mail.data = buffer.slice(0, end);
        buffer = buffer.slice(end + 5);
        mails.push(mail);
        mail = { from: null, to: [], data: '' };
        inData = false;
        say('250 kept');
        continue;
      }

      const lineEnd = buffer.indexOf('\r\n');
      if (lineEnd < 0) return;
      const line = buffer.slice(0, lineEnd);
      buffer = buffer.slice(lineEnd + 2);
      const command = line.toUpperCase();

      if (command.startsWith('EHLO') || command.startsWith('HELO')) say('250 fake-smtp');
      else if (command.startsWith('MAIL FROM:')) { mail.from = line.slice(10).trim(); say('250 ok'); }
      else if (command.startsWith('RCPT TO:')) { mail.to.push(line.slice(8).trim().replace(/[<>]/g, '')); say('250 ok'); }
      else if (command === 'DATA') { inData = true; say('354 go ahead'); }
      else if (command === 'QUIT') { say('221 bye'); socket.end(); }
      else say('250 ok');
    }
  });
}).listen(2525);

// ── Push ────────────────────────────────────────────────────────────────────
http.createServer((req, res) => {
  const chunks = [];
  req.on('data', c => chunks.push(c));
  req.on('end', () => {
    if (req.url.includes('/gone/')) { res.writeHead(410).end(); return; }

    pushes.push({ url: req.url, method: req.method, headers: req.headers, body: Buffer.concat(chunks).toString('base64') });
    res.writeHead(201).end();
  });
}).listen(2526);

// ── What was captured ───────────────────────────────────────────────────────
http.createServer((req, res) => {
  res.setHeader('Content-Type', 'application/json');
  if (req.url === '/mails') res.end(JSON.stringify(mails));
  else if (req.url === '/pushes') res.end(JSON.stringify(pushes));
  else { res.statusCode = 404; res.end('{}'); }
}).listen(2527);

console.log('fake mail server on 2525, fake push service on 2526, captured data on 2527');
