const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2]);
const port = Number(process.argv[3] || "8900");
const mimeTypes = {
  ".css": "text/css",
  ".data": "application/octet-stream",
  ".html": "text/html",
  ".js": "application/javascript",
  ".json": "application/json",
  ".png": "image/png",
  ".wasm": "application/wasm",
};

http.createServer((request, response) => {
  const requestedPath = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
  let filePath = path.resolve(root, `.${requestedPath}`);
  if (filePath !== root && !filePath.startsWith(`${root}${path.sep}`)) {
    response.writeHead(403).end();
    return;
  }
  if (fs.existsSync(filePath) && fs.statSync(filePath).isDirectory()) {
    filePath = path.join(filePath, "index.html");
  }
  fs.stat(filePath, (error, stats) => {
    if (error || !stats.isFile()) {
      response.writeHead(404).end();
      return;
    }
    let logicalPath = filePath;
    if (logicalPath.endsWith(".gz")) {
      response.setHeader("Content-Encoding", "gzip");
      logicalPath = logicalPath.slice(0, -3);
    } else if (logicalPath.endsWith(".br")) {
      response.setHeader("Content-Encoding", "br");
      logicalPath = logicalPath.slice(0, -3);
    }
    response.setHeader("Content-Type", mimeTypes[path.extname(logicalPath)] || "application/octet-stream");
    response.setHeader("Cross-Origin-Opener-Policy", "same-origin");
    response.setHeader("Cross-Origin-Embedder-Policy", "require-corp");
    response.writeHead(200);
    fs.createReadStream(filePath).pipe(response);
  });
}).listen(port, "127.0.0.1", () => {
  console.log(`Serving ${root} at http://127.0.0.1:${port}/`);
});
