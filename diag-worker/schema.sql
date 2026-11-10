-- 测试版诊断上报，每次上报一行
CREATE TABLE IF NOT EXISTS reports (
    id          INTEGER PRIMARY KEY,
    received_at INTEGER NOT NULL,          -- 接收时间（Unix 秒）
    install_id  TEXT    NOT NULL,          -- 测试版首次启动时随机生成的安装 ID
    version     TEXT    NOT NULL,          -- 程序版本，如 1.0.4-dev
    flavor      TEXT    NOT NULL,          -- normal / runtime（普通版 / 带运行时版）
    trigger     TEXT    NOT NULL,          -- 触发原因：startup / switch / error / exit / pending
    has_error   INTEGER NOT NULL,          -- 是否包含异常
    model       TEXT,                      -- BIOS 产品名
    bios        TEXT,                      -- BIOS 版本
    windows     TEXT,                      -- Windows 版本
    log         TEXT    NOT NULL           -- 完整日志
);
CREATE INDEX IF NOT EXISTS idx_reports_received ON reports (received_at);
CREATE INDEX IF NOT EXISTS idx_reports_install ON reports (install_id, received_at);
