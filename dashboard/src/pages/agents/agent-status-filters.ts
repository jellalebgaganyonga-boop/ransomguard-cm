import type { AgentStatus } from '@/api/agents';

/**
 * The states an agent can actually be in, as the Agents page offers them.
 *
 * `provisioned` is in the API's enum as the column default and nothing ever writes it: enrolment
 * creates the agent `active` in the same statement, and the only other writers are the heartbeat
 * (`active`), the disconnect worker (`disconnected`) and the decommission button. It was offered
 * here as a filter that could only ever return an empty page, which teaches people not to trust
 * the page, so it is gone -- see grid/tests/api/test_agent_counts.py.
 */
export const STATUS_FILTERS: Array<{ value: AgentStatus | ''; labelKey: string; fallback: string }> = [
  { value: '',              labelKey: 'common.all',          fallback: 'All' },
  { value: 'active',        labelKey: 'agents.list.active',  fallback: 'Active' },
  { value: 'disconnected',  labelKey: 'agents.list.disconnected', fallback: 'Disconnected' },
  { value: 'decommissioned',labelKey: 'agents.list.decommissioned',fallback: 'Decommissioned' },
];
