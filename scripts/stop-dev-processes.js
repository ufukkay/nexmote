const cp = require("child_process");

const pids = new Set();

const netstat = cp.execFileSync("netstat", ["-ano"], { encoding: "utf8" });
for (const line of netstat.split(/\r?\n/)) {
  if ((line.includes(":5080") || line.includes(":5173")) && line.includes("LISTENING")) {
    const parts = line.trim().split(/\s+/);
    pids.add(parts[parts.length - 1]);
  }
}

const tasklist = cp.execFileSync("tasklist", ["/FO", "CSV"], { encoding: "utf8" });
for (const line of tasklist.split(/\r?\n/)) {
  if (line.toLowerCase().includes("nexmote.agent.windows.exe") || line.toLowerCase().includes("nexmote.agent.tray.exe")) {
    const cols = line.split(",").map((value) => value.replace(/^"|"$/g, ""));
    if (cols[1]) {
      pids.add(cols[1]);
    }
  }
}

for (const pid of pids) {
  if (!pid || pid === "0") {
    continue;
  }

  console.log(`Stopping PID ${pid}`);
  try {
    cp.execFileSync("taskkill", ["/PID", pid, "/F"], { stdio: "inherit" });
  } catch (error) {
    console.error(`Could not stop PID ${pid}: ${error.message}`);
  }
}

if (pids.size === 0) {
  console.log("No NexMote dev processes found.");
}
