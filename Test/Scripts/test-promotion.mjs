import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const workflow = readFileSync(new URL('../../.github/workflows/promote_prerelease.yml', import.meta.url), 'utf8');
const source = workflow.split(/script: \|\r?\n/)[1].split(/\r?\n/).map(line => line.slice(12)).join('\n');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const execute = new AsyncFunction('github', 'context', 'console', source);
assert.match(workflow, /actions: read/);
assert.match(workflow, /issues: read/);
let count = 1;

async function check(name, options, expected) {
  let promoted = false;
  const release = {
    id: 1, tag_name: 'v1.2.4', prerelease: true, body: '',
    assets: [{ name: 'EricGameLauncher_1.2.4.zip', size: 1 }],
    published_at: new Date(Date.now() - 72 * 3600000).toISOString(),
    ...options.release,
  };
  const github = {
    rest: {
      repos: {
        listReleases: async () => ({ data: options.empty ? [] : [release] }),
        updateRelease: async args => { assert.equal(args.release_id, 1); promoted = true; },
      },
      git: {
        getRef: async () => ({ data: { object: { type: options.annotated ? 'tag' : 'commit', sha: options.annotated ? 'tag-object' : 'release-commit' } } }),
        getTag: async () => ({ data: { object: { sha: 'release-commit' } } }),
      },
      actions: {
        listWorkflowRuns: async args => {
          assert.equal(args.head_sha, 'release-commit');
          assert.equal(args.workflow_id, 'post_release.yml');
          if (options.apiError) throw new Error('fixture API unavailable');
          assert.equal(args.status, undefined);
          const defaults = {id: 100, head_sha: 'release-commit', run_number: 100, run_attempt: 1, status: 'completed', conclusion: 'success', event: 'release'};
          return { data: { workflow_runs: (options.runs ?? [{}]).map(run => ({...defaults, ...run})) } };
        },
      },
      issues: { listForRepo() {} },
    },
    paginate: async (_method, args) => {
      assert.equal(args.labels, 'block-release');
      return options.blocked ? [{ number: 7 }] : [];
    },
  };
  try { await execute(github, { repo: { owner: 'fixture', repo: 'fixture' } }, { log() {} }); }
  catch (error) { if (!options.apiError) throw error; }
  assert.equal(promoted, expected, name);
  count++;
  console.log('PASS ' + name);
}

await check('empty release list is skipped', { empty: true }, false);
await check('stable release is skipped', { release: { prerelease: false } }, false);
await check('young prerelease is skipped', { release: { published_at: new Date().toISOString() } }, false);
await check('explicit no-release marker blocks promotion', { release: { body: '[NO-RELEASE]' } }, false);
await check('absent validation blocks promotion', { runs: [] }, false);
await check('failed validation blocks promotion', { runs: [{ conclusion: 'failure' }] }, false);
await check('older success cannot hide newest failure', { runs: [{run_number: 100, conclusion: 'failure'}, {run_number: 99}] }, false);
await check('running validation cannot fall back to older success', { runs: [{run_number: 100, status: 'in_progress', conclusion: null}, {run_number: 99}] }, false);
await check('queued validation blocks promotion', { runs: [{status: 'queued', conclusion: null}] }, false);
await check('latest retry failure overrides previous attempt', { runs: [{run_attempt: 1}, {run_attempt: 2, conclusion: 'failure'}] }, false);
await check('PR validation cannot authorize promotion', { runs: [{event: 'pull_request'}] }, false);
await check('manual validation cannot authorize promotion', { runs: [{event: 'workflow_dispatch'}] }, false);
await check('push validation cannot authorize promotion', { runs: [{event: 'push'}] }, false);
await check('mismatched commit cannot authorize promotion', { runs: [{head_sha: 'other-commit'}] }, false);
await check('successful newest validation supersedes old failure', { runs: [{run_number: 99, conclusion: 'failure'}, {run_number: 100}] }, true);
await check('missing release package blocks promotion', { release: { assets: [] } }, false);
await check('blocking issue prevents promotion', { blocked: true }, false);
await check('API failure prevents promotion', { apiError: true }, false);
await check('validated release can be promoted', {}, true);
await check('annotated tag validates the commit', { annotated: true }, true);
console.log(`${count} checks passed.`);
