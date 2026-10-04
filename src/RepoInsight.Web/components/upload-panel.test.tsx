import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { expect, it } from "vitest";
import { UploadPanel } from "./upload-panel";
const actions = { onSelect: () => {}, onAnalyze: () => {}, onCancel: () => {} };
it("disables analysis until a file is selected", () => {
  const markup = renderToStaticMarkup(
    <UploadPanel {...actions} file={null} busy={false} />,
  );
  expect(markup).toContain('class="primary-button" disabled=""');
  expect(markup).toContain('accept=".zip,application/zip"');
});
it("shows the selected name without interpreting HTML", () => {
  const markup = renderToStaticMarkup(
    <UploadPanel
      {...actions}
      file={new File(["zip"], "<script>.zip")}
      busy={false}
    />,
  );
  expect(markup).toContain("&lt;script&gt;.zip");
  expect(markup).not.toContain('class="primary-button" disabled=""');
});
it("announces loading and offers cancellation", () => {
  const markup = renderToStaticMarkup(
    <UploadPanel {...actions} file={new File(["zip"], "repo.zip")} busy />,
  );
  expect(markup).toContain('role="status"');
  expect(markup).toContain("Cancel analysis");
  expect(markup).toContain('class="primary-button" disabled=""');
});
