import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ticketsApi, usersApi, ApiClientError } from '../services/api';
import { Ticket, TicketPriority, TicketStatus, EscalationEvent, User } from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  Search,
  PlusCircle,
  RefreshCw,
  AlertCircle,
  Inbox,
  X,
  Send,
  AlertTriangle,
  ArrowRight,
  UserCheck,
  Shield,
  ArrowUpDown,
  CheckCircle2,
} from 'lucide-react';

interface TicketsPageProps {
  mode?: 'all' | 'assigned' | 'escalations';
}

export const TicketsPage: React.FC<TicketsPageProps> = ({ mode = 'all' }) => {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  // Ticket Operations State
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 15;

  // Escalations State
  const [escalations, setEscalations] = useState<EscalationEvent[]>([]);

  // Filters & Sorting State
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [priorityFilter, setPriorityFilter] = useState<string>('');
  const [sortBy, setSortBy] = useState<string>('CreatedAt');
  const [sortDescending, setSortDescending] = useState<boolean>(true);

  // Status & Error
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);

  // Assignment Modal State (Admin only)
  const [assigningTicket, setAssigningTicket] = useState<Ticket | null>(null);
  const [availableAgents, setAvailableAgents] = useState<User[]>([]);
  const [selectedAgentId, setSelectedAgentId] = useState<number | ''>('');
  const [isLoadingAgents, setIsLoadingAgents] = useState(false);
  const [isSubmittingAssignment, setIsSubmittingAssignment] = useState(false);
  const [assignmentError, setAssignmentError] = useState<string | null>(null);

  // Create Ticket Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [newPriority, setNewPriority] = useState<TicketPriority>('Medium');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  // Fetch Tickets or Escalations based on mode
  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      if (mode === 'escalations') {
        const data = await ticketsApi.getEscalations();
        setEscalations(data);
      } else if (mode === 'assigned') {
        const res = await ticketsApi.getAssigned({
          search: searchTerm,
          status: statusFilter as TicketStatus | '',
          priority: priorityFilter as TicketPriority | '',
          sortBy,
          sortDescending,
          page,
          pageSize,
        });
        setTickets(res.items);
        setTotalCount(res.totalCount);
      } else {
        const res = await ticketsApi.getAll({
          search: searchTerm,
          status: statusFilter as TicketStatus | '',
          priority: priorityFilter as TicketPriority | '',
          sortBy,
          sortDescending,
          page,
          pageSize,
        });
        setTickets(res.items);
        setTotalCount(res.totalCount);
      }
    } catch (err: unknown) {
      if (err instanceof ApiClientError && err.isForbidden) {
        setError('Access denied: You do not have permission to view these tickets.');
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to retrieve ticket records.';
        setError(msg);
      }
    } finally {
      setIsLoading(false);
    }
  }, [mode, searchTerm, statusFilter, priorityFilter, sortBy, sortDescending, page]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  // Open assignment modal and fetch real agents
  const handleOpenAssignModal = async (ticket: Ticket) => {
    setAssigningTicket(ticket);
    setSelectedAgentId(ticket.assignedToUserId || '');
    setAssignmentError(null);
    setIsLoadingAgents(true);
    try {
      const allUsers = await usersApi.getAll();
      const agents = allUsers.filter((u) => u.role === 'Agent');
      setAvailableAgents(agents);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load agent directory.';
      setAssignmentError(msg);
    } finally {
      setIsLoadingAgents(false);
    }
  };

  const handleConfirmAssignment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!assigningTicket || !selectedAgentId) return;

    setIsSubmittingAssignment(true);
    setAssignmentError(null);
    try {
      await ticketsApi.assign(assigningTicket.id, Number(selectedAgentId));
      setSuccessNotice(`Ticket #${assigningTicket.id} successfully assigned to Agent #${selectedAgentId}.`);
      setAssigningTicket(null);
      fetchData();
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        setAssignmentError(err.message);
      } else {
        const msg = err instanceof Error ? err.message : 'Failed to assign ticket.';
        setAssignmentError(msg);
      }
    } finally {
      setIsSubmittingAssignment(false);
    }
  };

  // Submit new ticket
  const handleCreateTicket = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim() || !newDescription.trim()) {
      setModalError('Title and description are required.');
      return;
    }

    setIsSubmitting(true);
    setModalError(null);
    try {
      await ticketsApi.create({
        title: newTitle.trim(),
        description: newDescription.trim(),
        priority: newPriority,
      });
      setIsModalOpen(false);
      setNewTitle('');
      setNewDescription('');
      setNewPriority('Medium');
      fetchData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create ticket.';
      setModalError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            {mode === 'escalations' ? (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                <AlertTriangle className="w-3.5 h-3.5" />
                SLA Compliance Audit
              </span>
            ) : isAdmin ? (
              <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-purple-500/10 text-purple-400 border border-purple-500/20">
                <Shield className="w-3.5 h-3.5" />
                Admin Ticket Operations
              </span>
            ) : null}
          </div>
          <h2 className="text-xl font-bold text-white mt-1">
            {mode === 'assigned'
              ? 'Assigned Support Tickets'
              : mode === 'escalations'
              ? 'SLA Escalations & Breach Queue'
              : isAdmin
              ? 'Enterprise Ticket Operations'
              : 'Support Tickets'}
          </h2>
          <p className="text-sm text-slate-400 mt-1">
            {mode === 'escalations'
              ? 'Real-time record of all tickets that have breached first-response or resolution SLAs.'
              : 'Browse, search, sort, and triage tickets with live SLA deadlines and audit histories.'}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={fetchData}
            disabled={isLoading}
            className="inline-flex items-center gap-2 px-3.5 py-2.5 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition disabled:opacity-50"
            title="Refresh"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            <span className="hidden sm:inline">Refresh</span>
          </button>
          {mode !== 'escalations' && (
            <button
              onClick={() => setIsModalOpen(true)}
              className="inline-flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 shadow-md shadow-indigo-600/20 transition"
            >
              <PlusCircle className="w-4 h-4" />
              <span>Create Ticket</span>
            </button>
          )}
        </div>
      </div>

      {/* Global Alerts */}
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

      {successNotice && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-300 text-sm flex items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
            <span>{successNotice}</span>
          </div>
          <button onClick={() => setSuccessNotice(null)} className="text-emerald-400 hover:text-emerald-200">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Filters Bar for standard ticket list */}
      {mode !== 'escalations' && (
        <div className="flex flex-col sm:flex-row gap-3">
          <div className="relative flex-1">
            <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              type="text"
              placeholder="Search by title, description, or #ID..."
              value={searchTerm}
              onChange={(e) => {
                setSearchTerm(e.target.value);
                setPage(1);
              }}
              className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
          <select
            value={statusFilter}
            onChange={(e) => {
              setStatusFilter(e.target.value);
              setPage(1);
            }}
            className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
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
            className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">All Priorities</option>
            <option value="Critical">Critical</option>
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value)}
            className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="CreatedAt">Sort by Created</option>
            <option value="Priority">Sort by Priority</option>
            <option value="Status">Sort by Status</option>
            <option value="Title">Sort by Title</option>
            <option value="Id">Sort by ID</option>
          </select>
          <button
            onClick={() => setSortDescending((d) => !d)}
            className="px-3 py-2 rounded-xl bg-slate-900 border border-slate-800 text-slate-300 hover:text-white transition flex items-center gap-1.5 text-xs font-semibold"
            title="Toggle Sort Direction"
          >
            <ArrowUpDown className="w-3.5 h-3.5" />
            <span>{sortDescending ? 'Desc' : 'Asc'}</span>
          </button>
        </div>
      )}

      {/* Main Content Table */}
      {mode === 'escalations' ? (
        // Escalations View
        <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
          <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
            <h3 className="font-semibold text-white text-sm">Escalated Tickets Log</h3>
            <span className="text-xs text-slate-400">{escalations.length} total breach events</span>
          </div>

          {isLoading ? (
            <div className="py-16 flex flex-col items-center justify-center gap-3 text-slate-400">
              <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
              <p className="text-sm">Querying SLA breach events...</p>
            </div>
          ) : escalations.length === 0 ? (
            <div className="py-16 flex flex-col items-center justify-center text-center px-4">
              <CheckCircle2 className="w-12 h-12 text-emerald-500/50 mb-3" />
              <h4 className="text-base font-semibold text-slate-200">No Escalations Recorded</h4>
              <p className="text-sm text-slate-400 max-w-sm mt-1">
                All tickets are currently within acceptable SLA response and resolution milestones.
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                    <th className="py-3 px-6">Ticket ID</th>
                    <th className="py-3 px-6">Subject / Title</th>
                    <th className="py-3 px-6">Priority</th>
                    <th className="py-3 px-6">Status</th>
                    <th className="py-3 px-6">Assigned Agent</th>
                    <th className="py-3 px-6">Escalation Reason</th>
                    <th className="py-3 px-6">Escalated At</th>
                    <th className="py-3 px-6 text-right">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 text-sm">
                  {escalations.map((esc, idx) => (
                    <tr key={`${esc.ticketId}-${idx}`} className="hover:bg-slate-800/30 transition-colors">
                      <td className="py-3.5 px-6 font-mono text-xs text-indigo-400 font-semibold">
                        <Link to={`/tickets/${esc.ticketId}`} className="hover:underline">
                          #{esc.ticketId}
                        </Link>
                      </td>
                      <td className="py-3.5 px-6 font-medium text-slate-200 max-w-xs truncate">
                        {esc.ticketTitle}
                      </td>
                      <td className="py-3.5 px-6">
                        <PriorityBadge priority={esc.priority} />
                      </td>
                      <td className="py-3.5 px-6">
                        <StatusBadge status={esc.status} />
                      </td>
                      <td className="py-3.5 px-6 text-xs text-slate-300">
                        {esc.assignedToName || (esc.assignedToUserId ? `Agent #${esc.assignedToUserId}` : 'Unassigned')}
                      </td>
                      <td className="py-3.5 px-6 text-xs text-rose-400 font-mono">
                        {esc.reason}
                      </td>
                      <td className="py-3.5 px-6 text-xs text-slate-400 font-mono">
                        {new Date(esc.escalatedAt).toLocaleString()}
                      </td>
                      <td className="py-3.5 px-6 text-right">
                        <Link
                          to={`/tickets/${esc.ticketId}`}
                          className="inline-flex items-center gap-1 px-3 py-1 rounded-lg text-xs font-semibold text-indigo-400 hover:text-indigo-300 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 transition"
                        >
                          <span>Inspect</span>
                          <ArrowRight className="w-3 h-3" />
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : (
        // Standard Ticket Operations View
        <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
          <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
            <h3 className="font-semibold text-white text-sm">Ticket Queue</h3>
            <span className="text-xs text-slate-400">Total: {totalCount} tickets</span>
          </div>

          {isLoading ? (
            <div className="py-16 flex flex-col items-center justify-center gap-3 text-slate-400">
              <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
              <p className="text-sm">Loading tickets...</p>
            </div>
          ) : tickets.length === 0 ? (
            <div className="py-16 flex flex-col items-center justify-center text-center px-4">
              <Inbox className="w-12 h-12 text-slate-600 mb-3" />
              <h4 className="text-base font-semibold text-slate-200">No tickets found</h4>
              <p className="text-sm text-slate-400 max-w-sm mt-1">
                No tickets matched your query parameters. Try broadening your search or clearing filters.
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                    <th className="py-3 px-6">ID</th>
                    <th className="py-3 px-6">Title & Summary</th>
                    <th className="py-3 px-6">Priority</th>
                    <th className="py-3 px-6">Status</th>
                    <th className="py-3 px-6">Creator</th>
                    <th className="py-3 px-6">Assignee</th>
                    <th className="py-3 px-6">Resolution Due</th>
                    <th className="py-3 px-6">Created</th>
                    {isAdmin && <th className="py-3 px-6 text-right">Admin Action</th>}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60 text-sm">
                  {tickets.map((ticket) => (
                    <tr key={ticket.id} className="hover:bg-slate-800/30 transition-colors">
                      <td className="py-3.5 px-6 font-mono text-xs text-indigo-400 font-semibold">
                        <Link to={`/tickets/${ticket.id}`} className="hover:underline">
                          #{ticket.id}
                        </Link>
                      </td>
                      <td className="py-3.5 px-6 font-medium text-slate-200">
                        <Link to={`/tickets/${ticket.id}`} className="hover:text-indigo-300 transition block">
                          <div>{ticket.title}</div>
                          <div className="text-xs text-slate-400 truncate max-w-xs">{ticket.description}</div>
                        </Link>
                      </td>
                      <td className="py-3.5 px-6">
                        <PriorityBadge priority={ticket.priority} />
                      </td>
                      <td className="py-3.5 px-6">
                        <StatusBadge status={ticket.status} />
                      </td>
                      <td className="py-3.5 px-6 font-mono text-xs text-slate-400">User #{ticket.createdByUserId}</td>
                      <td className="py-3.5 px-6 text-xs">
                        {ticket.assignedToUserId ? (
                          <span className="font-mono text-slate-300">Agent #{ticket.assignedToUserId}</span>
                        ) : (
                          <span className="text-amber-400 font-medium">Unassigned</span>
                        )}
                      </td>
                      <td className="py-3.5 px-6 text-xs font-mono text-slate-400">
                        {ticket.resolutionDueAt
                          ? new Date(ticket.resolutionDueAt).toLocaleString([], {
                              month: 'short',
                              day: 'numeric',
                              hour: '2-digit',
                              minute: '2-digit',
                            })
                          : '—'}
                      </td>
                      <td className="py-3.5 px-6 text-xs text-slate-400 font-mono">
                        {new Date(ticket.createdAt).toLocaleDateString()}
                      </td>
                      {isAdmin && (
                        <td className="py-3.5 px-6 text-right">
                          <button
                            onClick={() => handleOpenAssignModal(ticket)}
                            className="px-2.5 py-1 rounded-lg text-xs font-semibold text-purple-400 bg-purple-500/10 hover:bg-purple-500/20 border border-purple-500/30 transition inline-flex items-center gap-1"
                          >
                            <UserCheck className="w-3 h-3" />
                            <span>{ticket.assignedToUserId ? 'Reassign' : 'Assign'}</span>
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination Controls */}
          {totalCount > pageSize && (
            <div className="px-6 py-4 border-t border-slate-800 flex items-center justify-between text-xs text-slate-400">
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
                <span>
                  Page {page} of {Math.ceil(totalCount / pageSize)}
                </span>
                <button
                  onClick={() => setPage((p) => (p * pageSize < totalCount ? p + 1 : p))}
                  disabled={page * pageSize >= totalCount}
                  className="px-3 py-1.5 rounded-lg bg-slate-800 text-slate-300 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-slate-700 transition"
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Admin Ticket Assignment Modal */}
      {assigningTicket && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
              <h3 className="font-semibold text-white text-base flex items-center gap-2">
                <UserCheck className="w-5 h-5 text-purple-400" />
                Assign Ticket #{assigningTicket.id}
              </h3>
              <button
                onClick={() => setAssigningTicket(null)}
                disabled={isSubmittingAssignment}
                className="text-slate-400 hover:text-white disabled:opacity-50"
                aria-label="Close"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleConfirmAssignment} className="p-6 space-y-4">
              {assignmentError && (
                <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 shrink-0" />
                  <span>{assignmentError}</span>
                </div>
              )}

              <div>
                <p className="text-xs text-slate-400 mb-1">Ticket Subject:</p>
                <p className="text-sm font-semibold text-white truncate">{assigningTicket.title}</p>
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Select Support Agent
                </label>
                {isLoadingAgents ? (
                  <div className="py-4 text-center text-xs text-slate-400">Loading agents...</div>
                ) : availableAgents.length === 0 ? (
                  <div className="p-3 rounded-xl bg-amber-500/10 text-amber-300 text-xs border border-amber-500/20">
                    No users with the Agent role found. Please promote an employee in User Management first.
                  </div>
                ) : (
                  <select
                    required
                    data-testid="agent-select"
                    aria-label="Select Support Agent"
                    value={selectedAgentId}
                    onChange={(e) => setSelectedAgentId(Number(e.target.value))}
                    className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 focus:outline-none focus:ring-2 focus:ring-purple-500"
                  >
                    <option value="">-- Choose Agent --</option>
                    {availableAgents.map((agent) => (
                      <option key={agent.id} value={agent.id}>
                        {agent.fullName} ({agent.email}) [ID #{agent.id}]
                      </option>
                    ))}
                  </select>
                )}
              </div>

              <div className="pt-2 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setAssigningTicket(null)}
                  disabled={isSubmittingAssignment}
                  className="px-4 py-2 rounded-xl text-sm font-medium text-slate-300 hover:bg-slate-800 disabled:opacity-50 transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmittingAssignment || !selectedAgentId || availableAgents.length === 0}
                  className="px-5 py-2 rounded-xl text-sm font-semibold text-white bg-purple-600 hover:bg-purple-500 disabled:opacity-50 transition flex items-center gap-2"
                >
                  {isSubmittingAssignment ? (
                    <>
                      <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                      <span>Assigning...</span>
                    </>
                  ) : (
                    <>
                      <UserCheck className="w-4 h-4" />
                      <span>Confirm Assignment</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Create Ticket Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
              <h3 className="font-semibold text-white text-base flex items-center gap-2">
                <PlusCircle className="w-5 h-5 text-indigo-400" />
                Submit New Ticket
              </h3>
              <button onClick={() => setIsModalOpen(false)} className="text-slate-400 hover:text-white" aria-label="Close">
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleCreateTicket} className="p-6 space-y-4">
              {modalError && (
                <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 shrink-0" />
                  <span>{modalError}</span>
                </div>
              )}

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Title
                </label>
                <input
                  type="text"
                  required
                  maxLength={200}
                  value={newTitle}
                  onChange={(e) => setNewTitle(e.target.value)}
                  placeholder="Summary of the issue"
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Priority
                </label>
                <select
                  value={newPriority}
                  onChange={(e) => setNewPriority(e.target.value as TicketPriority)}
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                >
                  <option value="Low">Low — Inquiries, cosmetic requests (72h SLA)</option>
                  <option value="Medium">Medium — Standard business request (24h SLA)</option>
                  <option value="High">High — Major department disruption (8h SLA)</option>
                  <option value="Critical">Critical — Severe system outage (4h SLA)</option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Description
                </label>
                <textarea
                  rows={4}
                  required
                  maxLength={4000}
                  value={newDescription}
                  onChange={(e) => setNewDescription(e.target.value)}
                  placeholder="Provide all relevant details..."
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              <div className="pt-2 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 rounded-xl text-sm font-medium text-slate-300 hover:bg-slate-800 transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmitting}
                  className="px-5 py-2 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 transition flex items-center gap-2"
                >
                  {isSubmitting ? (
                    <>
                      <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                      <span>Submitting...</span>
                    </>
                  ) : (
                    <>
                      <Send className="w-4 h-4" />
                      <span>Submit Ticket</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
