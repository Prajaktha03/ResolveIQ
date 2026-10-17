import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ticketsApi } from '../services/api';
import { Ticket, TicketDashboardResponse, TicketStatus } from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  CheckCircle2,
  AlertTriangle,
  PlayCircle,
  RefreshCw,
  AlertCircle,
  Inbox,
  Filter,
  Search,
  Eye,
  ArrowRight,
  Flame,
  Clock,
  ShieldAlert,
} from 'lucide-react';

export const AgentDashboard: React.FC = () => {
  const { user } = useAuth();

  const [metrics, setMetrics] = useState<TicketDashboardResponse | null>(null);
  const [assignedTickets, setAssignedTickets] = useState<Ticket[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [priorityFilter, setPriorityFilter] = useState<string>('');

  const [actionInProgressId, setActionInProgressId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [metricsData, assignedData] = await Promise.all([
        ticketsApi.getDashboard(),
        ticketsApi.getAssigned({
          search: searchTerm.trim() || undefined,
          status: (statusFilter as TicketStatus) || undefined,
          priority: (priorityFilter as any) || undefined,
          page: 1,
          pageSize: 15,
        }),
      ]);
      setMetrics(metricsData);
      setAssignedTickets(assignedData.items);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load assigned ticket queue.';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [searchTerm, statusFilter, priorityFilter]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleStatusUpdate = async (ticket: Ticket, newStatus: TicketStatus) => {
    setActionInProgressId(ticket.id);
    setActionError(null);
    try {
      // Backend UpdateTicketRequest requires both Status and Priority. Pass ticket.priority to preserve it.
      await ticketsApi.update(ticket.id, {
        status: newStatus,
        priority: ticket.priority,
      });
      fetchData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update ticket status.';
      setActionError(`Ticket #${ticket.id}: ${msg}`);
    } finally {
      setActionInProgressId(null);
    }
  };

  const total = metrics?.totalTickets ?? metrics?.TotalTickets ?? 0;
  const open = metrics?.openTickets ?? metrics?.OpenTickets ?? 0;
  const inProgress = metrics?.inProgressTickets ?? metrics?.InProgressTickets ?? 0;
  const atRisk = metrics?.atRiskTickets ?? metrics?.AtRiskTickets ?? 0;
  const breached = metrics?.breachedTickets ?? metrics?.BreachedTickets ?? 0;

  const urgentCount = assignedTickets.filter(
    (t) => (t.priority === 'Critical' || t.priority === 'High') && t.status !== 'Resolved'
  ).length;

  if (error && !metrics) {
    return (
      <div className="max-w-2xl mx-auto py-16 text-center space-y-4">
        <div className="w-16 h-16 rounded-2xl bg-rose-500/10 border border-rose-500/20 text-rose-400 flex items-center justify-center mx-auto">
          <AlertCircle className="w-8 h-8" />
        </div>
        <h2 className="text-xl font-bold text-white">Failed to Load Dashboard</h2>
        <p className="text-sm text-rose-300 max-w-md mx-auto">{error}</p>
        <button
          onClick={fetchData}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition"
        >
          <RefreshCw className="w-4 h-4" />
          <span>Retry</span>
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Welcome Header */}
      <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 shadow-sm">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              Support Desk
            </span>
          </div>
          <h1 className="text-xl font-bold text-white mt-1">
            Agent Desk — {user?.fullName}
          </h1>
          <p className="text-sm text-slate-400 mt-0.5">
            Manage customer incidents assigned to you, respond within SLA targets, and update resolution states.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={fetchData}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            <span>Refresh Queue</span>
          </button>
          <Link
            to="/agent/assigned"
            className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold shadow-md shadow-indigo-600/20 transition"
          >
            <span>All Assigned</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>
      </div>

      {/* Metrics Row */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3.5">
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Assigned Total</div>
          <div className="mt-2 text-2xl font-bold text-white">{metrics ? total : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-sky-400 uppercase tracking-wider">Open</div>
          <div className="mt-2 text-2xl font-bold text-sky-400">{metrics ? open : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-amber-400 uppercase tracking-wider">In Progress</div>
          <div className="mt-2 text-2xl font-bold text-amber-400">{metrics ? inProgress : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-indigo-400 uppercase tracking-wider flex items-center gap-1">
            <Flame className="w-3 h-3 text-indigo-400" />
            <span>High/Critical</span>
          </div>
          <div className="mt-2 text-2xl font-bold text-indigo-400">{urgentCount}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-amber-400 uppercase tracking-wider flex items-center gap-1.5">
            <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse"></span>
            At Risk
          </div>
          <div className="mt-2 text-2xl font-bold text-amber-400">{metrics ? atRisk : '—'}</div>
        </div>
        <div className="bg-slate-900 p-4 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-rose-500 uppercase tracking-wider flex items-center gap-1.5">
            <ShieldAlert className="w-3 h-3 text-rose-500" />
            Breached SLA
          </div>
          <div className="mt-2 text-2xl font-bold text-rose-500">{metrics ? breached : '—'}</div>
        </div>
      </div>

      {/* SLA Alert banner if any breached or at risk */}
      {metrics && (breached > 0 || atRisk > 0) && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center gap-3">
          <AlertTriangle className="w-5 h-5 text-rose-400 shrink-0" />
          <span>
            <strong className="font-bold text-rose-200">SLA Alert: </strong>
            You have <strong className="text-amber-300">{atRisk} ticket(s) at risk</strong> and{' '}
            <strong className="text-rose-400 font-bold">{breached} breached ticket(s)</strong> in your assigned queue. Prioritize high-impact incidents to meet resolution targets.
          </span>
        </div>
      )}

      {/* Quick Action Error Alert */}
      {actionError && (
        <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{actionError}</span>
        </div>
      )}

      {/* Filter & Search Toolbar */}
      <div className="bg-slate-900/90 p-4 rounded-2xl border border-slate-800 flex flex-col sm:flex-row gap-3">
        <div className="relative flex-1">
          <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            placeholder="Search assigned tickets by title, description, or #ID..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>

        <div className="flex flex-wrap sm:flex-nowrap gap-2">
          <div className="flex items-center gap-1.5 bg-slate-800 px-3 py-1.5 rounded-xl border border-slate-700">
            <Filter className="w-3.5 h-3.5 text-slate-400" />
            <select
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              className="bg-transparent text-xs text-slate-200 focus:outline-none"
              aria-label="Status filter"
            >
              <option value="" className="bg-slate-800">All Statuses</option>
              <option value="Open" className="bg-slate-800">Open</option>
              <option value="InProgress" className="bg-slate-800">In Progress</option>
              <option value="Resolved" className="bg-slate-800">Resolved</option>
            </select>
          </div>

          <div className="flex items-center gap-1.5 bg-slate-800 px-3 py-1.5 rounded-xl border border-slate-700">
            <select
              value={priorityFilter}
              onChange={(e) => setPriorityFilter(e.target.value)}
              className="bg-transparent text-xs text-slate-200 focus:outline-none"
              aria-label="Priority filter"
            >
              <option value="" className="bg-slate-800">All Priorities</option>
              <option value="Critical" className="bg-slate-800">Critical</option>
              <option value="High" className="bg-slate-800">High</option>
              <option value="Medium" className="bg-slate-800">Medium</option>
              <option value="Low" className="bg-slate-800">Low</option>
            </select>
          </div>
        </div>
      </div>

      {/* Error State with Retry */}
      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
            <span>{error}</span>
          </div>
          <button
            onClick={fetchData}
            className="px-3 py-1.5 rounded-lg bg-rose-500/20 hover:bg-rose-500/30 text-rose-200 text-xs font-semibold border border-rose-500/30 transition shrink-0"
          >
            Retry
          </button>
        </div>
      )}

      {/* Assigned Tickets Table */}
      <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
        <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
          <h2 className="font-semibold text-white text-sm">Assigned Ticket Queue</h2>
          <span className="text-xs text-slate-400">{assignedTickets.length} tickets matching filters</span>
        </div>

        {isLoading ? (
          <div className="py-16 flex flex-col items-center justify-center gap-3 text-slate-400">
            <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
            <p className="text-sm">Fetching assigned queue...</p>
          </div>
        ) : assignedTickets.length === 0 ? (
          <div className="py-16 flex flex-col items-center justify-center text-center px-4">
            <Inbox className="w-12 h-12 text-slate-600 mb-3" />
            <h3 className="text-base font-semibold text-slate-200">No tickets found</h3>
            <p className="text-sm text-slate-400 max-w-sm mt-1">
              {searchTerm || statusFilter || priorityFilter
                ? 'No assigned tickets match your active filter criteria.'
                : 'Your assigned queue is currently empty. Incoming tickets will appear here once assigned.'}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                  <th className="py-3 px-6">ID</th>
                  <th className="py-3 px-6">Ticket Title</th>
                  <th className="py-3 px-6">Priority</th>
                  <th className="py-3 px-6">Status</th>
                  <th className="py-3 px-6">Resolution Due</th>
                  <th className="py-3 px-6 text-right">Quick Transition</th>
                  <th className="py-3 px-6 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 text-sm">
                {assignedTickets.map((ticket) => {
                  const isActionLoading = actionInProgressId === ticket.id;
                  const isBreached =
                    ticket.resolutionDueAt &&
                    new Date(ticket.resolutionDueAt) < new Date() &&
                    ticket.status !== 'Resolved';

                  return (
                    <tr key={ticket.id} className="hover:bg-slate-800/30 transition-colors">
                      <td className="py-3.5 px-6 font-mono text-xs text-slate-400">
                        #{ticket.id}
                      </td>
                      <td className="py-3.5 px-6 font-medium text-slate-200 max-w-xs">
                        <Link
                          to={`/agent/tickets/${ticket.id}`}
                          className="hover:text-indigo-400 transition"
                        >
                          {ticket.title}
                        </Link>
                        <div className="text-xs text-slate-400 truncate max-w-xs">{ticket.description}</div>
                      </td>
                      <td className="py-3.5 px-6">
                        <PriorityBadge priority={ticket.priority} />
                      </td>
                      <td className="py-3.5 px-6">
                        <StatusBadge status={ticket.status} />
                      </td>
                      <td className="py-3.5 px-6 text-xs font-mono">
                        {ticket.resolutionDueAt ? (
                          <div className="flex items-center gap-1.5">
                            <Clock className={`w-3.5 h-3.5 ${isBreached ? 'text-rose-400' : 'text-slate-500'}`} />
                            <span className={isBreached ? 'text-rose-400 font-semibold' : 'text-slate-400'}>
                              {new Date(ticket.resolutionDueAt).toLocaleString([], {
                                month: 'short',
                                day: 'numeric',
                                hour: '2-digit',
                                minute: '2-digit',
                              })}
                            </span>
                          </div>
                        ) : (
                          <span className="text-slate-500">—</span>
                        )}
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        <div className="flex items-center justify-end gap-2">
                          {ticket.status === 'Open' && (
                            <button
                              onClick={() => handleStatusUpdate(ticket, 'InProgress')}
                              disabled={isActionLoading}
                              className="px-2.5 py-1 rounded-lg text-xs font-semibold text-amber-400 bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/30 transition flex items-center gap-1.5"
                            >
                              <PlayCircle className="w-3.5 h-3.5" />
                              <span>Start Work</span>
                            </button>
                          )}
                          {ticket.status === 'InProgress' && (
                            <button
                              onClick={() => handleStatusUpdate(ticket, 'Resolved')}
                              disabled={isActionLoading}
                              className="px-2.5 py-1 rounded-lg text-xs font-semibold text-emerald-400 bg-emerald-500/10 hover:bg-emerald-500/20 border border-emerald-500/30 transition flex items-center gap-1.5"
                            >
                              <CheckCircle2 className="w-3.5 h-3.5" />
                              <span>Resolve</span>
                            </button>
                          )}
                          {ticket.status === 'Resolved' && (
                            <span className="text-xs text-slate-400 font-medium italic">Completed</span>
                          )}
                        </div>
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        <Link
                          to={`/agent/tickets/${ticket.id}`}
                          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 transition"
                        >
                          <Eye className="w-3.5 h-3.5" />
                          <span>Details</span>
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {assignedTickets.length > 0 && (
          <div className="px-6 py-3.5 border-t border-slate-800 flex items-center justify-between text-xs">
            <span className="text-slate-400">Showing top queue incidents</span>
            <Link
              to="/agent/assigned"
              className="text-indigo-400 hover:text-indigo-300 font-semibold flex items-center gap-1 transition"
            >
              <span>View Full Assigned Workspace &rarr;</span>
            </Link>
          </div>
        )}
      </div>
    </div>
  );
};
