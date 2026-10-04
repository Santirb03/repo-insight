import type { Severity } from "./contracts";
export const severityPresentation: Record<
  Severity,
  { label: string; className: string }
> = {
  High: { label: "High", className: "severity-high" },
  Medium: { label: "Medium", className: "severity-medium" },
  Low: { label: "Low", className: "severity-low" },
  Info: { label: "Info", className: "severity-info" },
};
export const categoryLabels: Record<string, string> = {
  DataAccess: "Data access",
  DevOps: "DevOps & infrastructure",
  ApiIntegration: "APIs & integrations",
  BuildTool: "Build & packages",
};
