/**
 * Render 15_ci_badge.png: the recent GitHub Actions runs on this branch.
 *
 * This is NOT a screenshot of the github.com page. The browser in this
 * environment cannot reach github.com, so the image is rendered from the run
 * data the GitHub API returns through `gh run list`, and says so on its face.
 * Every run id, commit, conclusion and timestamp below comes from that API.
 *
 *   gh run list --branch sprint8/recovery --limit 10 \
 *     --json databaseId,name,headSha,status,conclusion,createdAt,displayTitle,event \
 *     > runs.json
 *   node scripts/capture-ci-card.mjs runs.json
 */

import { chromium } from 'playwright';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

const OUT_DIR = path.resolve(process.cwd(), '..', 'docs', 'screenshots');
const REPO = 'jellalebgaganyonga-boop/ransomguard-cm';
const BRANCH = 'sprint8/recovery';

const escape = (s) =>
  String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function rows(runs) {
  return runs
    .map((r) => {
      const ok = r.conclusion === 'success';
      return `<tr>
        <td class="mark ${ok ? 'ok' : 'bad'}">${ok ? '&#10003;' : '&#10007;'}</td>
        <td class="name">${escape(r.name)}<span class="title">${escape(r.displayTitle ?? '')}</span></td>
        <td><code>${escape(r.headSha.slice(0, 7))}</code></td>
        <td><code>${escape(r.event)}</code></td>
        <td class="${ok ? 'ok' : 'bad'}">${escape(r.conclusion ?? r.status)}</td>
        <td class="when">${escape(r.createdAt.replace('T', ' ').replace('Z', ' UTC'))}</td>
        <td><code>#${escape(r.databaseId)}</code></td>
      </tr>`;
    })
    .join('\n');
}

function html(runs) {
  const green = runs.filter((r) => r.conclusion === 'success').length;
  return `<!doctype html><html lang="en"><head><meta charset="utf-8">
<style>
  body { margin:0; width:1440px; height:900px; display:flex; align-items:center; justify-content:center;
         background:#f1f5f9; font-family:"Segoe UI",system-ui,sans-serif; color:#0f172a; }
  .card { width:1240px; background:#fff; border:1px solid #e2e8f0; border-radius:12px; padding:40px 44px; }
  h1 { font-size:27px; margin:0 0 4px; letter-spacing:-0.5px; }
  .sub { color:#64748b; font-size:14px; margin-bottom:26px; }
  table { width:100%; border-collapse:collapse; font-size:15px; }
  th { text-align:left; font-size:11px; letter-spacing:.08em; text-transform:uppercase; color:#64748b;
       padding:0 10px 9px; border-bottom:1px solid #e2e8f0; }
  td { padding:12px 10px; border-bottom:1px solid #f1f5f9; }
  td.mark { font-size:17px; width:26px; }
  .ok { color:#047857; font-weight:600; }
  .bad { color:#b91c1c; font-weight:600; }
  td.name { font-weight:600; }
  .title { display:block; font-weight:400; font-size:12px; color:#64748b; margin-top:2px;
           max-width:460px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  code { font-family:ui-monospace,Consolas,monospace; font-size:13px; color:#475569; }
  td.when { color:#64748b; font-size:13px; }
  .pill { display:inline-block; background:#ecfdf5; color:#047857; border:1px solid #a7f3d0;
          border-radius:999px; padding:3px 11px; font-size:12px; font-weight:600; }
  .foot { margin-top:22px; padding-top:16px; border-top:1px solid #e2e8f0; color:#64748b;
          font-size:12.5px; line-height:1.6; }
</style></head><body><div class="card">
  <h1>GitHub Actions &mdash; <code style="font-size:22px">${escape(BRANCH)}</code></h1>
  <div class="sub">${escape(REPO)} &mdash; last ${runs.length} workflow runs &nbsp;<span class="pill">${green}/${runs.length} success</span></div>
  <table><thead><tr><th></th><th>Workflow</th><th>Commit</th><th>Event</th><th>Result</th><th>Started</th><th>Run</th></tr></thead>
  <tbody>${rows(runs)}</tbody></table>
  <div class="foot">
    Rendered from the GitHub REST API via <code>gh run list --branch ${escape(BRANCH)}</code>, not a capture of the
    github.com page: the browser in this environment has no route to github.com. Run ids are real and resolve at
    <code>github.com/${escape(REPO)}/actions/runs/&lt;id&gt;</code>.
  </div>
</div></body></html>`;
}

async function main() {
  const source = process.argv[2];
  if (!source) throw new Error('usage: node capture-ci-card.mjs <runs.json>');
  const runs = JSON.parse(await readFile(source, 'utf-8'));
  await mkdir(OUT_DIR, { recursive: true });

  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  await page.setContent(html(runs), { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT_DIR, '15_ci_badge.png') });
  console.log('  15_ci_badge.png');
  await browser.close();
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
