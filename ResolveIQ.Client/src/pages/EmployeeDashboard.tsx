import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ticketsApi } from '../services/api';
import { Ticket, TicketDashboardResponse, TicketPriority } from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  PlusCircle,
  AlertCircle,
  Search,
  RefreshCw,
  X,
  Send,
  Inbox,
  Eye,
} from 'lucide-react';

export const EmployeeDashboard: React.FC = () => {
  const { user } = useAuth();

  const [dashboardMetrics, setDashboardMetrics] = useState<TicketDashboardResponse | null>(null);
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters & Search
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('');

  // Create Ticket Modal
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [newPriority, setNewPriority] = useState<TicketPriority>('Medium');
  const [isSubmittingTicket, setIsSubmittingTicket] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const [metricsData, ticketsData] = await Promise.all([
        ticketsApi.getDashboard(),
        ticketsApi.getAll({ search: searchTerm, status: statusFilter as any, page: 1, pageSize: 10 }),
      ]);
      setDashboardMetrics(metricsData);
      setTickets(ticketsData.items);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load tickets.';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  }, [searchTerm, statusFilter]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleCreateTicket = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle.trim() || !newDescription.trim()) {
      setModalError('Title and description are required.');
      return;
    }

    setIsSubmittingTicket(true);
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
      setIsSubmittingTicket(false);
    }
  };

  const total = dashboardMetrics?.totalTickets ?? dashboardMetrics?.TotalTickets ?? 0;
  const open = dashboardMetrics?.openTickets ?? dashboardMetrics?.OpenTickets ?? 0;
  const inProgress = dashboardMetrics?.inProgressTickets ?? dashboardMetrics?.InProgressTickets ?? 0;
  const resolved = dashboardMetrics?.resolvedTickets ?? dashboardMetrics?.ResolvedTickets ?? 0;

  return (
    <div className="space-y-6">
      {/* Welcome & Action Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div>
          <h2 className="text-xl font-bold text-white">
            Welcome back, {user?.fullName.split(' ')[0]} 👋
          </h2>
          <p className="text-sm text-slate-400 mt-1">
            Track and submit IT support requests. Our support agents respond to high-priority tickets within SLA targets.
          </p>
        </div>
        <div className="flex items-center gap-2 self-start sm:self-auto shrink-0">
          <Link
            to="/employee/tickets/new"
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 shadow-md shadow-indigo-600/20 transition-all"
          >
            <PlusCircle className="w-4 h-4" />
            <span>New Support Ticket</span>
          </Link>
          <button
            onClick={() => setIsModalOpen(true)}
            className="px-3 py-2.5 rounded-xl text-xs font-semibold text-slate-300 hover:text-white bg-slate-800 hover:bg-slate-700 border border-slate-700 transition"
            title="Open quick submission modal"
          >
            Quick Form
          </button>
        </div>
      </div>

      {/* Metrics Row */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <div className="bg-slate-900/80 p-5 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Total Submitted</div>
          <div className="mt-2 text-2xl font-bold text-white">
            {dashboardMetrics ? total : '—'}
          </div>
        </div>
        <div className="bg-slate-900/80 p-5 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-sky-400 uppercase tracking-wider">Open</div>
          <div className="mt-2 text-2xl font-bold text-sky-400">
            {dashboardMetrics ? open : '—'}
          </div>
        </div>
        <div className="bg-slate-900/80 p-5 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-amber-400 uppercase tracking-wider">In Progress</div>
          <div className="mt-2 text-2xl font-bold text-amber-400">
            {dashboardMetrics ? inProgress : '—'}
          </div>
        </div>
        <div className="bg-slate-900/80 p-5 rounded-2xl border border-slate-800">
          <div className="text-xs font-semibold text-emerald-400 uppercase tracking-wider">Resolved</div>
          <div className="mt-2 text-2xl font-bold text-emerald-400">
            {dashboardMetrics ? resolved : '—'}
          </div>
        </div>
      </div>

      {/* Search & Filters */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="relative flex-1">
          <Search className="w-4 h-4 absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400" />
          <input
            type="text"
            placeholder="Search by ticket title, description, or #ID..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
          />
        </div>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="px-3.5 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        >
          <option value="">All Statuses</option>
          <option value="Open">Open</option>
          <option value="InProgress">In Progress</option>
          <option value="Resolved">Resolved</option>
        </select>
        <button
          onClick={fetchData}
          className="p-2 rounded-xl bg-slate-900 border border-slate-800 text-slate-400 hover:text-slate-200 transition"
          title="Refresh tickets"
        >
          <RefreshCw className={`w-5 h-5 ${isLoading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      {/* Error State */}
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

      {/* Tickets Table / List */}
      <div className="bg-slate-900 rounded-2xl border border-slate-800 overflow-hidden shadow-sm">
        <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
          <h3 className="font-semibold text-white text-sm">My Recent Support Requests</h3>
          <span className="text-xs text-slate-400">{tickets.length} tickets found</span>
        </div>

        {isLoading ? (
          <div className="py-16 flex flex-col items-center justify-center gap-3 text-slate-400">
            <div className="w-8 h-8 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
            <p className="text-sm">Loading tickets from ResolveIQ...</p>
          </div>
        ) : tickets.length === 0 ? (
          <div className="py-16 flex flex-col items-center justify-center text-center px-4">
            <Inbox className="w-12 h-12 text-slate-600 mb-3" />
            <h4 className="text-base font-semibold text-slate-200">No support tickets found</h4>
            <p className="text-sm text-slate-400 max-w-sm mt-1">
              You haven't submitted any tickets matching this criteria yet.
            </p>
            <button
              onClick={() => setIsModalOpen(true)}
              className="mt-4 px-4 py-2 rounded-xl text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-500 transition"
            >
              Submit Your First Ticket
            </button>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-slate-800/80 bg-slate-950/40 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">
                  <th className="py-3 px-6">ID</th>
                  <th className="py-3 px-6">Subject / Title</th>
                  <th className="py-3 px-6">Priority</th>
                  <th className="py-3 px-6">Status</th>
                  <th className="py-3 px-6">Submitted</th>
                  <th className="py-3 px-6">Resolution Target</th>
                  <th className="py-3 px-6 text-right">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 text-sm">
                {tickets.map((ticket) => (
                  <tr key={ticket.id} className="hover:bg-slate-800/30 transition-colors">
                    <td className="py-3.5 px-6 font-mono text-xs text-slate-400">#{ticket.id}</td>
                    <td className="py-3.5 px-6 font-medium text-slate-200">
                      <Link to={`/employee/tickets/${ticket.id}`} className="hover:text-indigo-400 transition">
                        {ticket.title}
                      </Link>
                      <div className="text-xs text-slate-400 truncate max-w-md">{ticket.description}</div>
                    </td>
                    <td className="py-3.5 px-6">
                      <PriorityBadge priority={ticket.priority} />
                    </td>
                    <td className="py-3.5 px-6">
                      <StatusBadge status={ticket.status} />
                    </td>
                    <td className="py-3.5 px-6 text-xs text-slate-400">
                      {new Date(ticket.createdAt).toLocaleDateString()}
                    </td>
                    <td className="py-3.5 px-6 text-xs font-mono text-slate-400">
                      {ticket.resolutionDueAt
                        ? new Date(ticket.resolutionDueAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
                        : '—'}
                    </td>
                    <td className="py-3.5 px-6 text-right">
                      <Link
                        to={`/employee/tickets/${ticket.id}`}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 transition"
                      >
                        <Eye className="w-3.5 h-3.5" />
                        <span>View Details</span>
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {tickets.length > 0 && (
          <div className="px-6 py-3.5 border-t border-slate-800 flex items-center justify-between text-xs">
            <span className="text-slate-400">Displaying your most recent support requests</span>
            <Link
              to="/employee/tickets"
              className="text-indigo-400 hover:text-indigo-300 font-semibold flex items-center gap-1 transition"
            >
              <span>View All My Tickets &rarr;</span>
            </Link>
          </div>
        )}
      </div>

      {/* Create Ticket Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
              <h3 className="font-semibold text-white text-base flex items-center gap-2">
                <PlusCircle className="w-5 h-5 text-indigo-400" />
                Submit New Support Ticket
              </h3>
              <button
                onClick={() => setIsModalOpen(false)}
                className="text-slate-400 hover:text-white"
                aria-label="Close"
              >
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
                  Issue Summary (Title)
                </label>
                <input
                  type="text"
                  required
                  maxLength={200}
                  value={newTitle}
                  onChange={(e) => setNewTitle(e.target.value)}
                  placeholder="e.g., Cannot connect to corporate VPN after password reset"
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Urgency / Priority
                </label>
                <select
                  value={newPriority}
                  onChange={(e) => setNewPriority(e.target.value as TicketPriority)}
                  className="w-full px-3.5 py-2.5 bg-slate-800 border border-slate-700 rounded-xl text-sm text-slate-100 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                >
                  <option value="Low">Low — General inquiry or cosmetic issue (72h target)</option>
                  <option value="Medium">Medium — Standard business request (24h target)</option>
                  <option value="High">High — Major productivity impediment (8h target)</option>
                  <option value="Critical">Critical — Organization-wide business outage (4h target)</option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider mb-2">
                  Detailed Description & Steps
                </label>
                <textarea
                  rows={4}
                  required
                  maxLength={4000}
                  value={newDescription}
                  onChange={(e) => setNewDescription(e.target.value)}
                  placeholder="Describe what occurred, any error messages displayed, and steps already attempted..."
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
                  disabled={isSubmittingTicket}
                  className="px-5 py-2 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 transition flex items-center gap-2"
                >
                  {isSubmittingTicket ? (
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
