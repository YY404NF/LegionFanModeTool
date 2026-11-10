// LegionFanModeTool 测试版诊断接收接口。
// 只接受 POST /lfmt/v1/report 的 JSON（可 gzip 压缩），校验格式和大小，按安装 ID 限流后写入 D1。
// 不记录 IP；不需要客户端密钥（客户端代码公开，放密钥也挡不住），靠格式校验、大小限制和限流防滥用。

const ROUTE = "/lfmt/v1/report";
const MAX_BODY_BYTES = 256 * 1024;        // 请求体（压缩后）上限
const MAX_DECODED_BYTES = 4 * 1024 * 1024; // 解压后上限，防止压缩炸弹
const HOURLY_LIMIT = 30;                   // 每个安装 ID 每小时最多上报次数
const RETENTION_DAYS = 90;

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const FLAVORS = new Set(["normal", "runtime"]);
const TRIGGERS = new Set(["startup", "switch", "error", "exit", "pending"]);

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);
    if (pathname !== ROUTE) return reply(404, "not found");
    if (request.method !== "POST") return reply(405, "method not allowed");

    const length = Number(request.headers.get("content-length"));
    if (!Number.isFinite(length) || length <= 0) return reply(411, "content-length required");
    if (length > MAX_BODY_BYTES) return reply(413, "too large");

    let report;
    try {
      let body = request.body;
      if (request.headers.get("content-encoding") === "gzip")
        body = body.pipeThrough(new DecompressionStream("gzip"));
      report = JSON.parse(await readText(body, MAX_DECODED_BYTES));
    } catch (error) {
      return reply(error instanceof RangeError ? 413 : 400, "invalid body");
    }

    const problem = validate(report);
    if (problem) return reply(400, problem);

    const now = Math.floor(Date.now() / 1000);
    const { count } = await env.DB
      .prepare("SELECT COUNT(*) AS count FROM reports WHERE install_id = ? AND received_at > ?")
      .bind(report.installId, now - 3600)
      .first();
    if (count >= HOURLY_LIMIT) return reply(429, "rate limited");

    await env.DB
      .prepare(`INSERT INTO reports (received_at, install_id, version, flavor, trigger, has_error, model, bios, windows, log)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
      .bind(now, report.installId, report.version, report.flavor, report.trigger, report.hasError ? 1 : 0,
            report.model ?? null, report.bios ?? null, report.windows ?? null, report.log)
      .run();
    return new Response(null, { status: 204 });
  },

  async scheduled(_controller, env) {
    const cutoff = Math.floor(Date.now() / 1000) - RETENTION_DAYS * 86400;
    await env.DB.prepare("DELETE FROM reports WHERE received_at < ?").bind(cutoff).run();
  },
};

/** 读取请求体为文本，超过 limit 字节时抛出 RangeError。 */
async function readText(stream, limit) {
  const reader = stream.getReader();
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > limit) {
      await reader.cancel();
      throw new RangeError("body too large");
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}

/** 返回第一个不合格的字段说明；全部合格返回 null。 */
function validate(report) {
  if (typeof report !== "object" || report === null || Array.isArray(report)) return "body must be an object";
  if (typeof report.installId !== "string" || !UUID.test(report.installId)) return "invalid installId";
  if (!isText(report.version, 64)) return "invalid version";
  if (!FLAVORS.has(report.flavor)) return "invalid flavor";
  if (!TRIGGERS.has(report.trigger)) return "invalid trigger";
  if (typeof report.hasError !== "boolean") return "invalid hasError";
  for (const key of ["model", "bios", "windows"])
    if (report[key] != null && !isText(report[key], 256)) return `invalid ${key}`;
  if (!isText(report.log, MAX_DECODED_BYTES)) return "invalid log";
  return null;
}

function isText(value, maxLength) {
  return typeof value === "string" && value.length > 0 && value.length <= maxLength;
}

function reply(status, message) {
  return new Response(JSON.stringify({ error: message }), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}
