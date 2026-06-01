// Aspire sets services__webapi__http__0 via service discovery; fall back for standalone ng serve
const target = process.env["services__webapi__http__0"] || "http://localhost:5000";

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
