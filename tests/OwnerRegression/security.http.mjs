// Run only against the isolated local probe described in AUDIT_REPORT.md.
import assert from 'node:assert/strict';
const base = 'http://127.0.0.1:5189';
let checks = 0;
function check(value, message) { assert(value, message); checks++; console.log('PASS: ' + message); }
async function get(path, options = {}) { return fetch(base + path, { redirect: 'manual', ...options }); }
for (const path of ['/Bookings/MyBookings', '/Dashboard/Staff', '/Dashboard/Admin', '/Dashboard/Owner', '/Payments/PendingVerification', '/Payments/PaymentProof/1', '/Payments/RefundProof/1', '/UserManagement', '/Profile', '/InternalProfile', '/Communications', '/Notifications']) {
  const response = await get(path);
  check(response.status === 302 && response.headers.get('location')?.includes('/Account/Login'), 'Anonymous direct access denied: ' + path);
}
const login = await get('/Account/Login');
check(login.status === 200, 'Login renders without database startup access');
check(login.headers.get('x-content-type-options') === 'nosniff', 'Content sniffing disabled');
check(login.headers.get('x-frame-options') === 'DENY', 'Framing denied');
check(login.headers.get('content-security-policy')?.includes("frame-ancestors 'none'"), 'CSP restricts framing');
check(login.headers.get('referrer-policy') === 'strict-origin-when-cross-origin', 'Referrer policy set');
const post = await get('/Account/Login', {method:'POST', body:new URLSearchParams({Email:'nobody@example.test',Password:'not-a-real-password'})});
check(post.status === 400, 'Cookie login POST without antiforgery token rejected');
check((await get('/uploads/payments/example.png')).status === 404, 'Legacy payment uploads cannot be served statically');
check((await get('/uploads/profiles/%2e%2e/payments/example.png')).status === 404, 'Profile subtree cannot traverse to financial uploads');
check((await get('/missing-security-probe-page')).status === 404, 'Missing route returns 404');
check((await get('/health')).status === 503, 'Readiness fails when database is unavailable');
let limited = false;
for (let i=0; i<25; i++) if ((await get('/Account/Login')).status === 429) { limited=true; break; }
check(limited, 'Account endpoints return 429 after rate limit');
console.log(`SUCCESS: ${checks} HTTP security checks passed.`);
