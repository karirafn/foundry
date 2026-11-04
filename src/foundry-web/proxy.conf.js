// DEV ONLY — secure: false disables TLS cert verification, which the ASP.NET dev certificate needs.
// Aspire sets services__webapi__https__0 and services__webapi__http__0 via service discovery.
// Prefer https: the WebApi's http endpoint answers with a 307 (UseHttpsRedirection) to its https
// origin, and the browser would follow that redirect cross-origin, bypassing this proxy and
// failing wherever it does not trust the dev certificate. Fall back to http, then to a fixed
// address for standalone ng serve.
const target =
  process.env["services__webapi__https__0"] ||
  process.env["services__webapi__http__0"] ||
  "http://localhost:5000";

module.exports = {
  "/api": {
    target,
    secure: false,
    changeOrigin: true,
  },
  "/hubs": {
    target,
    secure: false,
    changeOrigin: true,
    ws: true,
  },
};
