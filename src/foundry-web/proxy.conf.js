// DEV ONLY — TLS certificate verification is turned off only for loopback targets, because the
// ASP.NET dev certificate may not be trusted by this proxy. Any other host keeps verification on.
// Aspire sets services__webapi__https__0 and services__webapi__http__0 via service discovery.
// Prefer https: the WebApi's http endpoint answers with a 307 (UseHttpsRedirection) to its https
// origin, and the browser would follow that redirect cross-origin, bypassing this proxy and
// failing wherever it does not trust the dev certificate. Fall back to http, then to a fixed
// address for standalone ng serve.
const target =
  process.env["services__webapi__https__0"] ||
  process.env["services__webapi__http__0"] ||
  "http://localhost:5000";

const LOOPBACK_HOSTNAMES = ["localhost", "127.0.0.1", "[::1]", "::1"];
const secure = !LOOPBACK_HOSTNAMES.includes(new URL(target).hostname);

module.exports = {
  "/api": {
    target,
    secure,
    changeOrigin: true,
  },
  "/hubs": {
    target,
    secure,
    changeOrigin: true,
    ws: true,
  },
};
