import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { ticketsApi } from '../services/api';
import { Ticket, TicketPriority, TicketStatus } from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  Search,
  RefreshCw,
  AlertCircle,
  Inbox,
  Filter,
  ArrowUpDown,
  Clock,
  Eye,
  CheckCircle2,
  PlayCircle,
} from 'lucide-react';

export const AssignedTicketsPage: React.FC = () => {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 10;

  // Search & Filter state
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [priorityFilter, setPriorityFilter] = useState<string>('');
  const [sortBy, setSortBy] = useState<string>('createdAt');
  const [sortDescending, setSortDescending] = useState<boolean>(true);

  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Quick Action State
  const [actionInProgressId, setActionInProgressId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const fetchTickets = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const res = await ticketsApi.getAssigned({
        search: searchTerm.trim() || undefined,
        status: (statusFilter as TicketStatus) || undefined,
        priority: (priorityFilter as TicketPriority) || undefined,
        page,
        pageSize,
        sortBy,
        sortDescending,
      });

      setTickets(res.items);
      setTotalCount(res.totalCount);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to retrieve assigned tickets.';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [searchTerm, statusFilter, priorityFilter, page, sortBy, sortDescending]);

  useEffect(() => {
    fetchTickets();
  }, [fetchTickets]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    fetchTickets();
  };

  const handleSortToggle = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(true);
    }
    setPage(1);
  };

  const handleQuickStatusUpdate = async (ticket: Ticket, newStatus: TicketStatus) => {
    setActionInProgressId(ticket.id);
    setActionError(null);
    try {
      // Backend requires both Status and Priority; preserve ticket's priority
      await ticketsApi.update(ticket.id, {
        status: newStatus,
        priority: ticket.priority,
      });
      fetchTickets();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update ticket status.';
      setActionError(`Ticket #${ticket.id}: ${msg}`);
    } finally {
      setActionInProgressId(null);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize) || 1;

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm">
        <div>
          <div className="flex items-center gap-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
              <Filter className="w-3.5 h-3.5" />
              Agent Queue
            </span>
          </div>
          <h1 className="text-xl font-bold text-white mt-1">Assigned Tickets Workspace</h1>
          <p className="text-sm text-slate-400">
            Triage, investigate, and resolve all service tickets assigned directly to your agent account.
          </p>
        </div>

        <button
          onClick={fetchTickets}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition self-start sm:self-auto"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          <span>Refresh Queue</span>
        </button>
      </div>

      {/* Quick Action Error Alert */}
      {actionError && (
        <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{actionError}</span>
        </div>
      )}

      {/* Search & Filter Toolbar */}
      <div className="bg-slate-900/90 p-4 rounded-2xl border border-slate-800 space-y-3">
        <form onSubmit={handleSearchSubmit} className="flex flex-col sm:flex-row gap-3">
          <div className="relative flex-1">
            <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="text"
              placeholder="Search by ticket title, description, or #ID..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-10 pr-4 py-2 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>

          <div className="flex flex-wrap sm:flex-nowrap gap-2">
            <select
              value={statusFilter}
              onChange={(e) => {
                setStatusFilter(e.target.value);
                setPage(1);
              }}
              className="px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-xs text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
              aria-label="Filter by Status"
            >
              <option value="">All Statuses</option>
              <option value="Open">Open</option>
              <option value="InProgress">In Progress</option>
              <option value="Resolved">Resolved</option>
            </select>

            <select
              value={priorityFilter}
              onChange={(e) => {
                setPriorityFilter(e.target.value);
                setPage(1);
              }}
              className="px-3 py-2 bg-slate-800 border border-slate-700 rounded-xl text-xs text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
              aria-label="Filter by Priority"
            >
              <option value="">All Priorities</option>
              <option value="Critical">Critical</option>
              <option value="High">High</option>
              <option value="Medium">Medium</option>
              <option value="Low">Low</option>
            </select>

            <button
              type="submit"
              className="px-4 py-2 rounded-xl text-xs font-semibold bg-indigo-600 hover:bg-indigo-500 text-white transition"
            >
              Search
            </button>
          </div>
        </form>
      </div>

      {/* Error Banner with Retry */}
      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />
            <span>{error}</span>
          </div>
          <button
            onClick={fetchTickets}
            className="px-3 py-1.5 rounded-lg bg-rose-500/20 hover:bg-rose-500/30 text-rose-200 text-xs font-semibold border border-rose-500/30 transition shrink-0"
          >
            Retry
          </button>
        </div>
      )}

      {/* Tickets Table Card */}
      <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
        <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
          <h2 className="font-semibold text-white text-sm">
            {statusFilter || priorityFilter || searchTerm ? 'Filtered Assigned Queue' : 'All Assigned Tickets'}
          </h2>
          <span className="text-xs text-slate-400">{totalCount} total tickets</span>
        </div>

        {isLoading ? (
          <div className="py-20 flex flex-col items-center justify-center gap-3 text-slate-400">
            <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
            <p className="text-sm">Loading assigned tickets...</p>
          </div>
        ) : tickets.length === 0 ? (
          <div className="py-20 flex flex-col items-center justify-center text-center px-4">
            <Inbox className="w-12 h-12 text-slate-600 mb-3" />
            <h3 className="text-base font-semibold text-slate-200">No assigned tickets found</h3>
            <p className="text-sm text-slate-400 max-w-sm mt-1">
              {searchTerm || statusFilter || priorityFilter
                ? 'No tickets match your active filter criteria.'
                : 'You currently have no tickets assigned.'}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                  <th
                    className="py-3.5 px-6 cursor-pointer hover:text-slate-200"
                    onClick={() => handleSortToggle('id')}
                  >
                    <div className="flex items-center gap-1.5">
                      <span>ID</span>
                      <ArrowUpDown className="w-3 h-3 text-slate-500" />
                    </div>
                  </th>
                  <th
                    className="py-3.5 px-6 cursor-pointer hover:text-slate-200"
                    onClick={() => handleSortToggle('title')}
                  >
                    <div className="flex items-center gap-1.5">
                      <span>Title & Description</span>
                      <ArrowUpDown className="w-3 h-3 text-slate-500" />
                    </div>
                  </th>
                  <th
                    className="py-3.5 px-6 cursor-pointer hover:text-slate-200"
                    onClick={() => handleSortToggle('priority')}
                  >
                    <div className="flex items-center gap-1.5">
                      <span>Priority</span>
                      <ArrowUpDown className="w-3 h-3 text-slate-500" />
                    </div>
                  </th>
                  <th
                    className="py-3.5 px-6 cursor-pointer hover:text-slate-200"
                    onClick={() => handleSortToggle('status')}
                  >
                    <div className="flex items-center gap-1.5">
                      <span>Status</span>
                      <ArrowUpDown className="w-3 h-3 text-slate-500" />
                    </div>
                  </th>
                  <th className="py-3.5 px-6">Resolution Due</th>
                  <th
                    className="py-3.5 px-6 cursor-pointer hover:text-slate-200"
                    onClick={() => handleSortToggle('createdAt')}
                  >
                    <div className="flex items-center gap-1.5">
                      <span>Created</span>
                      <ArrowUpDown className="w-3 h-3 text-slate-500" />
                    </div>
                  </th>
                  <th className="py-3.5 px-6 text-right">Quick Transition</th>
                  <th className="py-3.5 px-6 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 text-sm">
                {tickets.map((ticket) => {
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
                      <td className="py-3.5 px-6 max-w-sm">
                        <Link
                          to={`/agent/tickets/${ticket.id}`}
                          className="font-medium text-slate-200 hover:text-indigo-400 transition"
                        >
                          {ticket.title}
                        </Link>
                        <div className="text-xs text-slate-400 truncate mt-0.5">{ticket.description}</div>
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
                      <td className="py-3.5 px-6 text-xs text-slate-400">
                        {new Date(ticket.createdAt).toLocaleDateString([], {
                          month: 'short',
                          day: 'numeric',
                        })}
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        <div className="flex items-center justify-end gap-1.5">
                          {ticket.status === 'Open' && (
                            <button
                              onClick={() => handleQuickStatusUpdate(ticket, 'InProgress')}
                              disabled={isActionLoading}
                              className="px-2.5 py-1 rounded-lg text-xs font-semibold text-amber-400 bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/30 transition flex items-center gap-1"
                            >
                              <PlayCircle className="w-3.5 h-3.5" />
                              <span>Start</span>
                            </button>
                          )}
                          {ticket.status === 'InProgress' && (
                            <button
                              onClick={() => handleQuickStatusUpdate(ticket, 'Resolved')}
                              disabled={isActionLoading}
                              className="px-2.5 py-1 rounded-lg text-xs font-semibold text-emerald-400 bg-emerald-500/10 hover:bg-emerald-500/20 border border-emerald-500/30 transition flex items-center gap-1"
                            >
                              <CheckCircle2 className="w-3.5 h-3.5" />
                              <span>Resolve</span>
                            </button>
                          )}
                          {ticket.status === 'Resolved' && (
                            <span className="text-xs text-slate-400 italic">Resolved</span>
                          )}
                        </div>
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        <Link
                          to={`/agent/tickets/${ticket.id}`}
                          className="inline-flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-semibold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 transition"
                        >
                          <Eye className="w-3.5 h-3.5" />
                          <span>View</span>
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination Controls */}
        {totalCount > pageSize && (
          <div className="px-6 py-4 border-t border-slate-800 flex flex-col sm:flex-row items-center justify-between gap-3 text-xs text-slate-400">
            <span>
              Showing {(page - 1) * pageSize + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} tickets
            </span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-slate-700 transition"
              >
                Previous
              </button>
              <span className="font-mono px-2">
                Page {page} of {totalPages}
              </span>
              <button
                onClick={() => setPage((p) => (p < totalPages ? p + 1 : p))}
                disabled={page >= totalPages}
                className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-slate-700 transition"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
