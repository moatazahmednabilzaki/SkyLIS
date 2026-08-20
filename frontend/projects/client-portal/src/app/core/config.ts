// Dev servers run on :4300 against the API on :5178 (CORS). Any other origin is a
// production deployment where nginx serves the portal and proxies /api and /hubs
// same-origin — no CORS, no hardcoded hosts.
export const API_BASE_URL =
  location.port === '4300' ? 'http://localhost:5178/api/v1' : '/api/v1';
