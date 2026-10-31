import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';

const workflow = readFileSync(new URL('../../.github/workflows/post_release.yml', import.meta.url), 'utf8');
assert.equal(existsSync(new URL('../../.github/workflows/validate.yml', import.meta.url)), false);
assert.doesNotMatch(workflow, /needs\.validate|needs: validate|^  validate:/m);
assert.match(workflow, /build:\r?\n    needs: prepare_release/);
assert.match(workflow, /needs: \[prepare_release, build, upload_asset, publish_notes\]/);
for (const name of readdirSync(new URL('../../.github/workflows/', import.meta.url))) {
  const contents = readFileSync(new URL('../../.github/workflows/' + name, import.meta.url), 'utf8');
  assert.doesNotMatch(contents, /dotnet test|Test\/Scripts\/validate\.ps1|workflows\/validate\.yml/);
}
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
function script(job) {
  const section = workflow.split(new RegExp(`\\r?\\n  ${job}:`))[1].split(/\r?\n  [a-z_]+:/)[0];
  const source = section.split(/script: \|\r?\n/)[1].split(/\r?\n/).map(line => line.slice(12)).join('\n');
  return new AsyncFunction('github', 'context', 'require', source);
}
const prepare = script('prepare_release');
const finalize = script('publish_notes');
const pending = '正在构建，稍后将在此显示更新日志和软件包';
const notes = '# Tagged release notes\n\nVerified package is ready.\n';
let checks = 0;
async function check(name, execute, options = {}) {
  const release = {id: 17, tag_name: 'v1.2.5', published_at: '2026-10-20T12:00:00Z'};
  const updates = [];
  let error;
  const github = {rest: {repos: {
    getRelease: async args => {
      assert.deepEqual(args, {owner: 'fixture', repo: 'fixture', release_id: 17});
      if (options.missingRelease) throw Object.assign(new Error('missing release'), {status: 404});
      return {data: {...release, draft: false, ...options.current}};
    },
    updateRelease: async args => {
      if (options.denied) throw Object.assign(new Error('permission denied'), {status: 403});
      assert.deepEqual(args, {owner: 'fixture', repo: 'fixture', release_id: 17, body: options.body});
      updates.push(args);
    },
  }}};
  const requireFixture = module => {
    assert.equal(module, 'fs');
    return {readFileSync(path, encoding) {
      assert.equal(path, '.releasenote.md');
      assert.equal(encoding, 'utf8');
      return options.notes ?? notes;
    }};
  };
  try { await execute(github, {repo: {owner: 'fixture', repo: 'fixture'}, payload: {release}}, requireFixture); }
  catch (caught) { error = caught; }
  assert.equal(Boolean(error), Boolean(options.error), name);
  assert.equal(updates.length, options.error ? 0 : 1, name);
  checks++;
  console.log('PASS ' + name);
}
await check('preparation replaces only the body with the exact requested message', prepare, {body: pending});
await check('finalization writes the complete tagged notes with original newlines', finalize, {body: notes});
for (const [name, execute] of [['preparation', prepare], ['finalization', finalize]]) {
  await check(`${name} protects a republished release`, execute, {current: {published_at: '2026-10-20T13:00:00Z'}, error: true});
  await check(`${name} protects a different tag`, execute, {current: {tag_name: 'v9.9.9'}, error: true});
  await check(`${name} does not publish or modify a draft`, execute, {current: {draft: true}, error: true});
  await check(`${name} does not recreate a deleted release`, execute, {missingRelease: true, error: true});
  await check(`${name} propagates update permission errors`, execute, {denied: true, error: true});
}
await check('empty final notes are rejected before modifying the body', finalize, {notes: ' \r\n ', error: true});
console.log(`${checks} checks passed.`);
