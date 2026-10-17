import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { ticketsApi, healthApi } from '../services/api';
import { Ticket, TicketDashboardResponse, EscalationEvent } from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  Users,
  AlertTriangle,
  Server,
  Database,
  ArrowRight,
  RefreshCw,
  AlertCircle,
  ShieldCheck,
  CheckCircle2,
  Layers,
} from 'lucide-react';

export const AdminDashboard: React.FC = () => {
  const [metrics, setMetrics] = useState<TicketDashboardResponse | null>(null);
  const [recentTickets, setRecentTickets] = useState<Ticket[]>([]);
  const [escalations, setEscalations] = useState<EscalationEvent[]>([]);
  const [healthStatus, setHealthStatus] = useState<{ status: string; database: string } | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const isMountedRef = React.useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [metricsRes, ticketsRes, escalationsRes, healthRes] = await Promise.allSettled([
        ticketsApi.getDashboard(),
        ticketsApi.getAll({ page: 1, pageSize: 6, sortBy: 'createdAt', sortDescending: true }),
        ticketsApi.getEscalations(),
        healthApi.checkReady(),
      ]);

      if (!isMountedRef.current) return;

      if (metricsRes.status === 'fulfilled') {
        setMetrics(metricsRes.value);
      } else {
        const err = metricsRes.reason;
        setError(err instanceof Error ? err.message : 'Failed to retrieve administrative metrics.');
      }

      if (ticketsRes.status === 'fulfilled') {
        setRecentTickets(ticketsRes.value.items);
      }

      if (escalationsRes.status === 'fulfilled') {
        setEscalations(escalationsRes.value);
      }

      if (healthRes.status === 'fulfilled') {
        setHealthStatus(healthRes.value);
      } else {
        setHealthStatus({ status: 'Degraded', database: 'Unreachable' });
      }
    } catch (err: unknown) {
      if (!isMountedRef.current) return;
      const msg = err instanceof Error ? err.message : 'Failed to load administrative overview.';
      setError(msg);
    } finally {
      if (isMountedRef.current) {
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const total = metrics?.totalTickets ?? metrics?.TotalTickets ?? 0;
  const open = metrics?.openTickets ?? metrics?.OpenTickets ?? 0;
  const inProgress = metrics?.inProgressTickets ?? metrics?.InProgressTickets ?? 0;
  const resolved = metrics?.resolvedTickets ?? metrics?.ResolvedTickets ?? 0;
  const atRisk = metrics?.atRiskTickets ?? metrics?.AtRiskTickets ?? 0;
  const breached = metrics?.breachedTickets ?? metrics?.BreachedTickets ?? 0;
  const compliance = metrics?.slaCompliancePercentage ?? metrics?.SlaCompliancePercentage ?? 0;
  const checksMet = metrics?.slaChecksMet ?? metrics?.SlaChecksMet ?? 0;
  const checksBreached = metrics?.slaChecksBreached ?? metrics?.SlaChecksBreached ?? 0;

  // Unassigned count from recent query
  const unassignedRecent = recentTickets.filter((t) => !t.assignedToUserId).length;

  return (
    <div className="space-y-6">
      {/* Top Banner & Quick Controls */}
      <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-purple-500/10 text-purple-400 border border-purple-500/20">
              <ShieldCheck className="w-3.5 h-3.5" />
              Administrator Control Center
            </span>
          </div>
          <h2 className="text-xl font-bold text-white mt-1">IT Service Desk Operations</h2>
          <p className="text-sm text-slate-400">
            Real-time health monitoring, operational ticket velocity, and team SLA compliance across ResolveIQ.
          </p>
        </div>
        <button
          onClick={fetchData}
          disabled={isLoading}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition self-start sm:self-auto disabled:opacity-50"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          <span>Refresh Operations</span>
        </button>
      </div>

      {/* Error state with retry */}
      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
            <span>{error}</span>
          </div>
          <button
            onClick={fetchData}
            className="px-3 py-1 rounded-lg bg-rose-500/20 hover:bg-rose-500/30 text-rose-200 text-xs font-semibold border border-rose-500/30 transition shrink-0"
          >
            Retry
          </button>
        </div>
      )}

      {/* Operational Metric Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-6 gap-3.5">
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Total Tickets</div>
          <div className="mt-2 text-2xl font-bold text-white">{metrics ? total : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-sky-400 uppercase tracking-wider">Open</div>
          <div className="mt-2 text-2xl font-bold text-sky-400">{metrics ? open : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-amber-400 uppercase tracking-wider">In Progress</div>
          <div className="mt-2 text-2xl font-bold text-amber-400">{metrics ? inProgress : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-emerald-400 uppercase tracking-wider">Resolved</div>
          <div className="mt-2 text-2xl font-bold text-emerald-400">{metrics ? resolved : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-rose-400 uppercase tracking-wider flex items-center gap-1">
            <span className="w-1.5 h-1.5 rounded-full bg-rose-500 animate-pulse"></span>
            At Risk SLA
          </div>
          <div className="mt-2 text-2xl font-bold text-rose-400">{metrics ? atRisk : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-[11px] font-semibold text-red-500 uppercase tracking-wider">Breached SLA</div>
          <div className="mt-2 text-2xl font-bold text-red-500">{metrics ? breached : '—'}</div>
        </div>
      </div>

      {/* Enterprise SLA Performance & System Readiness */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* SLA Compliance Card */}
        <div className="bg-slate-900 p-5 rounded-2xl border border-slate-800 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">SLA Compliance</span>
              <span className="text-xs font-bold text-indigo-400 font-mono">
                {metrics ? `${compliance}%` : '—'}
              </span>
            </div>
            <div className="mt-3 w-full bg-slate-800 rounded-full h-2 overflow-hidden">
              <div
                className={`h-full transition-all duration-500 ${
                  compliance >= 90 ? 'bg-emerald-500' : compliance >= 75 ? 'bg-amber-500' : 'bg-rose-500'
                }`}
                style={{ width: `${Math.min(100, Math.max(0, compliance))}%` }}
              ></div>
            </div>
            <div className="mt-4 grid grid-cols-2 gap-2 text-xs">
              <div className="p-2.5 rounded-xl bg-slate-800/50 border border-slate-800">
                <span className="text-slate-400 block text-[10px] uppercase">Checks Met</span>
                <span className="text-emerald-400 font-bold text-base font-mono">{checksMet}</span>
              </div>
              <div className="p-2.5 rounded-xl bg-slate-800/50 border border-slate-800">
                <span className="text-slate-400 block text-[10px] uppercase">Breaches</span>
                <span className="text-rose-400 font-bold text-base font-mono">{checksBreached}</span>
              </div>
            </div>
          </div>
          <div className="mt-4 pt-3 border-t border-slate-800 flex items-center justify-between text-[11px] text-slate-400">
            <span>Recent Unassigned: {unassignedRecent}</span>
            <Link to="/tickets/escalations" className="text-indigo-400 hover:text-indigo-300 font-medium">
              View SLA Audit
            </Link>
          </div>
        </div>

        {/* System Health Card */}
        <div className="bg-slate-900 p-5 rounded-2xl border border-slate-800 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">System Readiness</span>
              <span
                className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                  healthStatus?.status === 'Healthy'
                    ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                    : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
                }`}
              >
                <span className={`w-1.5 h-1.5 rounded-full ${healthStatus?.status === 'Healthy' ? 'bg-emerald-400' : 'bg-amber-400'}`}></span>
                {healthStatus?.status || 'Checking...'}
              </span>
            </div>
            <div className="mt-4 space-y-2 text-xs">
              <div className="flex items-center justify-between text-slate-300">
                <span className="flex items-center gap-1.5 text-slate-400">
                  <Server className="w-3.5 h-3.5 text-indigo-400" />
                  API Gateway (.NET 10)
                </span>
                <span className="font-mono text-emerald-400">Online</span>
              </div>
              <div className="flex items-center justify-between text-slate-300">
                <span className="flex items-center gap-1.5 text-slate-400">
                  <Database className="w-3.5 h-3.5 text-indigo-400" />
                  PostgreSQL DB
                </span>
                <span className="font-mono text-emerald-400">{healthStatus?.database || 'Connected'}</span>
              </div>
            </div>
          </div>
          <div className="mt-4 pt-3 border-t border-slate-800 flex items-center justify-between text-[11px] text-slate-400">
            <span>Probe: <code>/health/ready</code></span>
            <Link to="/admin/health" className="text-indigo-400 hover:text-indigo-300 font-medium">
              Health Diagnostics
            </Link>
          </div>
        </div>

        {/* User Governance Quick Link */}
        <Link
          to="/admin/users"
          className="bg-slate-900 p-5 rounded-2xl border border-slate-800 hover:border-slate-700 transition flex flex-col justify-between group"
        >
          <div>
            <div className="w-9 h-9 rounded-xl bg-purple-500/10 text-purple-400 flex items-center justify-center mb-3">
              <Users className="w-5 h-5" />
            </div>
            <h4 className="text-base font-semibold text-white group-hover:text-purple-300 transition">User Governance</h4>
            <p className="text-xs text-slate-400 mt-1">
              Audit system accounts, promote employees to support agents, and inspect registered users.
            </p>
          </div>
          <div className="mt-4 flex items-center gap-1 text-xs font-semibold text-purple-400 group-hover:translate-x-1 transition-transform">
            <span>Manage Users</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </div>
        </Link>
      </div>

      {/* SLA Escalation Events & Recent Platform Incidents */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Escalation Queue Preview */}
        <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm flex flex-col">
          <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <AlertTriangle className="w-4 h-4 text-amber-400" />
              <h3 className="font-semibold text-white text-sm">Active SLA Breach Escalations</h3>
            </div>
            <Link to="/tickets/escalations" className="text-xs font-semibold text-indigo-400 hover:text-indigo-300 flex items-center gap-1">
              <span>View All</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>

          <div className="p-4 flex-1">
            {isLoading ? (
              <div className="py-12 flex flex-col items-center justify-center gap-2 text-slate-400">
                <div className="w-6 h-6 border-2 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
                <p className="text-xs">Checking escalation queue...</p>
              </div>
            ) : escalations.length === 0 ? (
              <div className="py-12 text-center text-slate-400 text-xs flex flex-col items-center justify-center gap-2">
                <CheckCircle2 className="w-8 h-8 text-emerald-400/60" />
                <span>No SLA breach escalation events on record.</span>
              </div>
            ) : (
              <div className="space-y-2.5">
                {escalations.slice(0, 4).map((esc, idx) => (
                  <Link
                    key={`${esc.ticketId}-${idx}`}
                    to={`/tickets/${esc.ticketId}`}
                    className="block p-3 rounded-xl bg-slate-800/40 hover:bg-slate-800/80 border border-slate-800 transition"
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-mono text-xs text-indigo-400 font-semibold">#{esc.ticketId}</span>
                      <PriorityBadge priority={esc.priority} />
                    </div>
                    <div className="mt-1 text-sm font-medium text-slate-200 truncate">{esc.ticketTitle}</div>
                    <div className="mt-1 flex items-center justify-between text-xs text-slate-400">
                      <span className="text-rose-400 truncate max-w-[200px]">{esc.reason}</span>
                      <span className="text-[11px] font-mono">{new Date(esc.escalatedAt).toLocaleDateString()}</span>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Recent Tickets Activity */}
        <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm flex flex-col">
          <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Layers className="w-4 h-4 text-indigo-400" />
              <h3 className="font-semibold text-white text-sm">Recent Platform Tickets</h3>
            </div>
            <Link to="/tickets" className="text-xs font-semibold text-indigo-400 hover:text-indigo-300 flex items-center gap-1">
              <span>View All Tickets</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>

          <div className="p-4 flex-1">
            {isLoading ? (
              <div className="py-12 flex flex-col items-center justify-center gap-2 text-slate-400">
                <div className="w-6 h-6 border-2 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
                <p className="text-xs">Querying ticket data...</p>
              </div>
            ) : recentTickets.length === 0 ? (
              <div className="py-12 text-center text-slate-400 text-xs">No tickets found in repository.</div>
            ) : (
              <div className="space-y-2.5">
                {recentTickets.slice(0, 4).map((ticket) => (
                  <Link
                    key={ticket.id}
                    to={`/tickets/${ticket.id}`}
                    className="block p-3 rounded-xl bg-slate-800/40 hover:bg-slate-800/80 border border-slate-800 transition"
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs text-slate-400">#{ticket.id}</span>
                        <StatusBadge status={ticket.status} />
                      </div>
                      <PriorityBadge priority={ticket.priority} />
                    </div>
                    <div className="mt-1 text-sm font-medium text-slate-200 truncate">{ticket.title}</div>
                    <div className="mt-1 flex items-center justify-between text-xs text-slate-400">
                      <span>{ticket.assignedToUserId ? `Assigned to Agent #${ticket.assignedToUserId}` : 'Unassigned'}</span>
                      <span className="text-[11px] font-mono">{new Date(ticket.createdAt).toLocaleDateString()}</span>
                    </div>
                  </Link>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
