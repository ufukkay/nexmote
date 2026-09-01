export type View =
  | "devices"
  | "device-detail"
  | "downloads"
  | "settings"
  | "users"
  | "audit-log"
  | "security-profiles"
  | "device-groups";

export type StatusFilter = "all" | "online" | "offline" | "warning";

export type DetailTab =
  | "overview"
  | "specs"
  | "performance"
  | "network"
  | "applications"
  | "updates"
  | "terminal"
  | "activity";

export type SortField =
  | "deviceName"
  | "status"
  | "activeUser"
  | "ipAddress"
  | "cpu"
  | "agentVersion"
  | "lastSeen";

export type SortDirection = "asc" | "desc";
