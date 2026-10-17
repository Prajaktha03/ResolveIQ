import React, { useState, useEffect, useCallback } from 'react';
import { useParams, Link, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { ticketsApi, commentsApi, aiApi, ApiClientError } from '../services/api';
import {
  Ticket,
  TicketStatus,
  TicketPriority,
  TicketComment,
  TicketHistory,
  TicketSlaStatus,
  TroubleshootingResult,
  PriorityRecommendationResult,
} from '../types';
import { PriorityBadge, StatusBadge } from '../components/Badge';
import {
  ArrowLeft,
  User,
  Sparkles,
  MessageSquare,
  History,
  AlertCircle,
  CheckCircle2,
  Send,
  Loader2,
  FileText,
  HelpCircle,
  BookOpen,
  CheckSquare,
  RefreshCw,
  ShieldAlert,
  Zap,
  Sliders,
  Check,
  Award,
} from 'lucide-react';

export const TicketDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const ticketId = id ? parseInt(id, 10) : NaN;
  const { user } = useAuth();

  const isAgentOrAdmin = user?.role === 'Agent' || user?.role === 'Admin';
  const backPath = isAgentOrAdmin ? '/agent/assigned' : '/employee/tickets';
  const backLabel = isAgentOrAdmin ? 'Back to Assigned Tickets' : 'Back to My Tickets';

  // Flash message passed from create ticket
  const [flashMessage, setFlashMessage] = useState<string | null>(
    (location.state as { message?: string })?.message || null
  );

  // Core Ticket Data
  const [ticket, setTicket] = useState<Ticket | null>(null);
  const [sla, setSla] = useState<TicketSlaStatus | null>(null);
  const [history, setHistory] = useState<TicketHistory[]>([]);
  const [comments, setComments] = useState<TicketComment[]>([]);

  // Agent Management State
  const [statusDraft, setStatusDraft] = useState<TicketStatus>('Open');
  const [priorityDraft, setPriorityDraft] = useState<TicketPriority>('Medium');
  const [isUpdatingTicket, setIsUpdatingTicket] = useState(false);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [updateSuccess, setUpdateSuccess] = useState<string | null>(null);

  // Page States
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isForbidden, setIsForbidden] = useState(false);
  const [isNotFound, setIsNotFound] = useState(false);

  // Active Tab: 'conversation' | 'history' | 'ai'
  const [activeTab, setActiveTab] = useState<'conversation' | 'history' | 'ai'>('conversation');

  // Comment Submission State
  const [newComment, setNewComment] = useState('');
  const [isSubmittingComment, setIsSubmittingComment] = useState(false);
  const [commentError, setCommentError] = useState<string | null>(null);

  // AI Troubleshooting State
  const [isTroubleshooting, setIsTroubleshooting] = useState(false);
  const [aiResult, setAiResult] = useState<TroubleshootingResult | null>(null);
  const [aiError, setAiError] = useState<string | null>(null);

  // AI Priority Advisor State
  const [isRecommendingPriority, setIsRecommendingPriority] = useState(false);
  const [priorityRec, setPriorityRec] = useState<PriorityRecommendationResult | null>(null);
  const [priorityRecError, setPriorityRecError] = useState<string | null>(null);

  const fetchTicketDetails = useCallback(async () => {
    if (isNaN(ticketId)) {
      setIsNotFound(true);
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    setError(null);
    setIsForbidden(false);
    setIsNotFound(false);

    try {
      // Fetch the primary ticket entity first
      const ticketData = await ticketsApi.getById(ticketId);
      setTicket(ticketData);
      setStatusDraft(ticketData.status);
      setPriorityDraft(ticketData.priority);

      // Concurrently fetch comments, SLA, and history with graceful degradation
      const [commentsRes, slaRes, historyRes] = await Promise.allSettled([
        commentsApi.getAll(ticketId),
        ticketsApi.getSla(ticketId),
        ticketsApi.getHistory(ticketId),
      ]);

      if (commentsRes.status === 'fulfilled') {
        setComments(commentsRes.value);
      }
      if (slaRes.status === 'fulfilled') {
        setSla(slaRes.value);
      }
      if (historyRes.status === 'fulfilled') {
        setHistory(historyRes.value);
      }
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        if (err.isForbidden) {
          setIsForbidden(true);
        } else if (err.isNotFound) {
          setIsNotFound(true);
        } else {
          setError(err.message);
        }
      } else {
        setError(err instanceof Error ? err.message : 'Failed to retrieve ticket details.');
      }
    } finally {
      setIsLoading(false);
    }
  }, [ticketId]);

  useEffect(() => {
    fetchTicketDetails();
  }, [fetchTicketDetails]);

  // Handle Agent Ticket Status / Priority Updates
  const handleUpdateTicket = async (overrideStatus?: TicketStatus, overridePriority?: TicketPriority) => {
    if (!ticket) return;

    const targetStatus = overrideStatus || statusDraft;
    const targetPriority = overridePriority || priorityDraft;

    setIsUpdatingTicket(true);
    setUpdateError(null);
    setUpdateSuccess(null);

    try {
      const updated = await ticketsApi.update(ticket.id, {
        status: targetStatus,
        priority: targetPriority,
      });

      setTicket(updated);
      setStatusDraft(updated.status);
      setPriorityDraft(updated.priority);
      setUpdateSuccess(`Ticket #${ticket.id} successfully updated to ${targetStatus} (${targetPriority} Priority).`);

      // Refresh SLA and history concurrently
      const [slaRes, historyRes] = await Promise.allSettled([
        ticketsApi.getSla(ticket.id),
        ticketsApi.getHistory(ticket.id),
      ]);

      if (slaRes.status === 'fulfilled') {
        setSla(slaRes.value);
      }
      if (historyRes.status === 'fulfilled') {
        setHistory(historyRes.value);
      }
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        if (err.isForbidden) {
          setUpdateError('Access Restricted: You are only authorized to update tickets assigned to your queue.');
        } else {
          setUpdateError(err.message || 'Failed to update ticket status or priority.');
        }
      } else {
        setUpdateError(err instanceof Error ? err.message : 'An unexpected error occurred while updating.');
      }
    } finally {
      setIsUpdatingTicket(false);
    }
  };

  // Handle New Comment Submission
  const handleAddComment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newComment.trim()) {
      setCommentError('Comment text cannot be empty.');
      return;
    }

    setIsSubmittingComment(true);
    setCommentError(null);

    try {
      const added = await commentsApi.create(ticketId, {
        content: newComment.trim(),
      });

      // Append server-confirmed comment to list
      setComments((prev) => [...prev, added]);
      setNewComment('');

      // Refresh SLA and audit history (agent comment may satisfy first-response milestone)
      const [slaRes, historyRes] = await Promise.allSettled([
        ticketsApi.getSla(ticketId),
        ticketsApi.getHistory(ticketId),
      ]);
      if (slaRes.status === 'fulfilled') setSla(slaRes.value);
      if (historyRes.status === 'fulfilled') setHistory(historyRes.value);
    } catch (err: unknown) {
      // Draft in newComment is deliberately preserved on failure
      if (err instanceof ApiClientError) {
        setCommentError(err.message || 'Failed to submit comment.');
      } else {
        setCommentError(err instanceof Error ? err.message : 'Error adding comment.');
      }
    } finally {
      setIsSubmittingComment(false);
    }
  };

  // Handle AI Troubleshooting Request
  const handleRunAiTroubleshooting = async () => {
    setIsTroubleshooting(true);
    setAiError(null);

    try {
      const result = await aiApi.troubleshoot(ticketId);
      setAiResult(result);
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        if (err.status === 503) {
          setAiError('ResolveIQ AI Engine is currently offline or disabled on the server. Ticket operations remain fully functional.');
        } else if (err.isRateLimited) {
          setAiError('AI request rate limit reached. Please wait a moment before trying again.');
        } else if (err.status === 504) {
          setAiError('AI troubleshooting analysis timed out. Please try again.');
        } else {
          setAiError(err.message || 'AI service returned an unexpected error.');
        }
      } else {
        setAiError(err instanceof Error ? err.message : 'Unable to connect to AI troubleshooting service.');
      }
    } finally {
      setIsTroubleshooting(false);
    }
  };

  // Handle AI Priority Recommendation Request
  const handleRunPriorityRecommendation = async () => {
    if (!ticket) return;
    setIsRecommendingPriority(true);
    setPriorityRecError(null);

    try {
      const result = await aiApi.recommendPriority({ ticketId: ticket.id });
      setPriorityRec(result);
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        if (err.status === 503) {
          setPriorityRecError('ResolveIQ AI Engine is currently offline or disabled on the server.');
        } else if (err.isRateLimited) {
          setPriorityRecError('AI request rate limit reached. Please wait a moment before trying again.');
        } else if (err.status === 504) {
          setPriorityRecError('AI priority analysis timed out. Please try again.');
        } else {
          setPriorityRecError(err.message || 'AI service returned an unexpected error.');
        }
      } else {
        setPriorityRecError(err instanceof Error ? err.message : 'Unable to connect to AI service.');
      }
    } finally {
      setIsRecommendingPriority(false);
    }
  };

  // 403 Forbidden State
  if (isForbidden) {
    return (
      <div className="max-w-2xl mx-auto py-16 text-center space-y-4">
        <div className="w-16 h-16 rounded-2xl bg-amber-500/10 border border-amber-500/20 text-amber-400 flex items-center justify-center mx-auto">
          <ShieldAlert className="w-8 h-8" />
        </div>
        <h2 className="text-xl font-bold text-white">Access Restricted</h2>
        <p className="text-sm text-slate-400 max-w-md mx-auto">
          You are not authorized to view Ticket #{ticketId}. In accordance with ResolveIQ security boundaries, employees may only access tickets they have submitted.
        </p>
        <Link
          to={backPath}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>{backLabel}</span>
        </Link>
      </div>
    );
  }

  // 404 Not Found State
  if (isNotFound) {
    return (
      <div className="max-w-2xl mx-auto py-16 text-center space-y-4">
        <div className="w-16 h-16 rounded-2xl bg-slate-800 border border-slate-700 text-slate-400 flex items-center justify-center mx-auto">
          <FileText className="w-8 h-8" />
        </div>
        <h2 className="text-xl font-bold text-white">Ticket Not Found</h2>
        <p className="text-sm text-slate-400 max-w-md mx-auto">
          Ticket #{ticketId} could not be located in ResolveIQ. It may have been removed or does not exist.
        </p>
        <Link
          to={backPath}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>{backLabel}</span>
        </Link>
      </div>
    );
  }

  // Loading State
  if (isLoading) {
    return (
      <div className="max-w-4xl mx-auto py-24 flex flex-col items-center justify-center gap-4 text-slate-400">
        <div className="w-10 h-10 border-4 border-indigo-500/20 border-t-indigo-500 rounded-full animate-spin"></div>
        <p className="text-sm">Loading ticket #{ticketId} details, SLA metrics, and conversation history...</p>
      </div>
    );
  }

  // General Error State
  if (error || !ticket) {
    return (
      <div className="max-w-2xl mx-auto py-16 text-center space-y-4">
        <div className="w-16 h-16 rounded-2xl bg-rose-500/10 border border-rose-500/20 text-rose-400 flex items-center justify-center mx-auto">
          <AlertCircle className="w-8 h-8" />
        </div>
        <h2 className="text-xl font-bold text-white">Failed to Load Ticket</h2>
        <p className="text-sm text-rose-300 max-w-md mx-auto">{error || 'An unexpected error occurred.'}</p>
        <div className="flex items-center justify-center gap-3">
          <button
            onClick={fetchTicketDetails}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-semibold border border-slate-700 transition"
          >
            <RefreshCw className="w-4 h-4" />
            <span>Retry</span>
          </button>
          <Link
            to={backPath}
            className="px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition"
          >
            {backLabel}
          </Link>
        </div>
      </div>
    );
  }

  // SLA Status badge helper
  const renderSlaBadge = (status?: string) => {
    if (!status) return null;
    const lower = status.toLowerCase();
    if (lower === 'met') {
      return (
        <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
          Met
        </span>
      );
    }
    if (lower === 'breached') {
      return (
        <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30">
          Breached
        </span>
      );
    }
    if (lower === 'approaching' || lower === 'atrisk') {
      return (
        <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/15 text-amber-400 border border-amber-500/30">
          At Risk
        </span>
      );
    }
    return (
      <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-indigo-500/15 text-indigo-400 border border-indigo-500/30">
        {status}
      </span>
    );
  };

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      {/* Flash Success Notification */}
      {flashMessage && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-300 text-sm flex items-center justify-between gap-3 animate-fade-in">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
            <span>{flashMessage}</span>
          </div>
          <button
            onClick={() => setFlashMessage(null)}
            className="text-emerald-400 hover:text-emerald-200 text-xs font-semibold"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Breadcrumb & Navigation */}
      <div className="flex items-center justify-between">
        <Link
          to={backPath}
          className="inline-flex items-center gap-2 text-sm text-slate-400 hover:text-slate-200 transition"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>{backLabel}</span>
        </Link>
        <button
          onClick={fetchTicketDetails}
          className="p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 text-xs transition flex items-center gap-1.5"
          title="Refresh ticket"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          <span>Refresh</span>
        </button>
      </div>

      {/* Ticket Header & Details Card */}
      <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2.5">
              <span className="font-mono text-xs font-bold text-slate-400 bg-slate-800/80 px-2.5 py-1 rounded-lg border border-slate-700">
                #{ticket.id}
              </span>
              <PriorityBadge priority={ticket.priority} />
              <StatusBadge status={ticket.status} />
            </div>
            <h1 className="text-xl sm:text-2xl font-bold text-white">{ticket.title}</h1>
          </div>

          <div className="flex flex-col sm:items-end gap-1.5">
            <div className="flex items-center gap-2 text-xs text-slate-400 bg-slate-800/50 p-2.5 rounded-xl border border-slate-800">
              <User className="w-4 h-4 text-slate-400" />
              <span>
                Assigned to:{' '}
                <strong className="text-slate-200 font-semibold">
                  {ticket.assignedToUserId ? `Agent #${ticket.assignedToUserId}` : 'Unassigned'}
                </strong>
              </span>
            </div>
            {ticket.assignedToUserId && user?.id === ticket.assignedToUserId && (
              <span className="text-[11px] font-semibold text-emerald-400 bg-emerald-500/10 px-2 py-0.5 rounded border border-emerald-500/20">
                Assigned to You
              </span>
            )}
          </div>
        </div>

        {/* Description */}
        <div className="pt-3 border-t border-slate-800/80">
          <h2 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
            Description
          </h2>
          <div className="p-4 rounded-xl bg-slate-950/40 border border-slate-800 text-slate-200 text-sm whitespace-pre-wrap leading-relaxed">
            {ticket.description}
          </div>
        </div>

        {/* SLA Information Bar (Phase 4) */}
        <div className="pt-3 border-t border-slate-800/80 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 text-xs">
          <div className="p-3 rounded-xl bg-slate-800/30 border border-slate-800">
            <span className="text-slate-400 block mb-1">Created At:</span>
            <span className="text-slate-200 font-mono">
              {new Date(ticket.createdAt).toLocaleString([], {
                month: 'short',
                day: 'numeric',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit',
              })}
            </span>
          </div>

          <div className="p-3 rounded-xl bg-slate-800/30 border border-slate-800">
            <span className="text-slate-400 block mb-1">First Response Milestone:</span>
            <div className="flex items-center justify-between mb-1">
              <span className="text-slate-200 font-mono text-[11px]">
                {ticket.firstResponseDueAt
                  ? `Due: ${new Date(ticket.firstResponseDueAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
                  : 'Due: —'}
              </span>
              {renderSlaBadge(sla?.firstResponseStatus)}
            </div>
            <div className="text-[10px] text-slate-400 font-mono">
              {ticket.firstRespondedAt
                ? `Responded: ${new Date(ticket.firstRespondedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
                : 'Awaiting first response'}
            </div>
          </div>

          <div className="p-3 rounded-xl bg-slate-800/30 border border-slate-800">
            <span className="text-slate-400 block mb-1">Resolution Milestone:</span>
            <div className="flex items-center justify-between mb-1">
              <span className="text-slate-200 font-mono text-[11px]">
                {ticket.resolutionDueAt
                  ? `Due: ${new Date(ticket.resolutionDueAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
                  : 'Due: —'}
              </span>
              {renderSlaBadge(sla?.resolutionStatus)}
            </div>
            <div className="text-[10px] text-slate-400 font-mono">
              {ticket.resolvedAt
                ? `Resolved: ${new Date(ticket.resolvedAt).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}`
                : 'Target resolution pending'}
            </div>
          </div>

          <div className="p-3 rounded-xl bg-slate-800/30 border border-slate-800">
            <span className="text-slate-400 block mb-1">Current Lifecycle:</span>
            <div className="flex items-center gap-2">
              <span className={`w-2 h-2 rounded-full ${ticket.status === 'Resolved' ? 'bg-emerald-400' : ticket.status === 'InProgress' ? 'bg-indigo-400 animate-pulse' : 'bg-slate-400'}`}></span>
              <span className="text-slate-200 font-semibold">{ticket.status}</span>
            </div>
            <div className="text-[10px] text-slate-400 mt-1">
              {ticket.status === 'Resolved' ? 'Closed incident' : 'Active queue target'}
            </div>
          </div>
        </div>
      </div>

      {/* Phase 3: Agent Ticket Management Control Panel */}
      {isAgentOrAdmin && (
        <div className="bg-slate-900 p-6 rounded-2xl border border-indigo-500/20 shadow-md space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 border-b border-slate-800 pb-3">
            <div className="flex items-center gap-2">
              <Sliders className="w-4 h-4 text-indigo-400" />
              <h3 className="text-sm font-bold text-white">Agent Operations & Ticket Controls</h3>
            </div>
            <span className="text-[11px] text-slate-400">
              Only updates to assigned tickets are permitted under ResolveIQ authorization policy.
            </span>
          </div>

          {updateSuccess && (
            <div className="p-3.5 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-300 text-xs flex items-center justify-between">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                <span>{updateSuccess}</span>
              </div>
              <button onClick={() => setUpdateSuccess(null)} className="text-emerald-400 hover:text-emerald-200 font-semibold text-xs">
                Dismiss
              </button>
            </div>
          )}

          {updateError && (
            <div className="p-3.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
              <span>{updateError}</span>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {/* Status Transition Control */}
            <div className="p-4 rounded-xl bg-slate-800/40 border border-slate-800 space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-300 uppercase tracking-wider">Ticket Status</span>
                <span className="text-[11px] text-slate-400">Current: {ticket.status}</span>
              </div>
              <div className="grid grid-cols-3 gap-2">
                {(['Open', 'InProgress', 'Resolved'] as TicketStatus[]).map((st) => (
                  <button
                    key={st}
                    type="button"
                    onClick={() => handleUpdateTicket(st, priorityDraft)}
                    disabled={isUpdatingTicket || ticket.status === st}
                    className={`px-3 py-2 rounded-xl text-xs font-semibold transition border ${
                      ticket.status === st
                        ? 'bg-indigo-600/20 border-indigo-500/50 text-indigo-300 cursor-default'
                        : 'bg-slate-800 hover:bg-slate-700/80 border-slate-700 text-slate-300'
                    } disabled:opacity-50`}
                  >
                    {isUpdatingTicket && statusDraft === st ? (
                      <Loader2 className="w-3.5 h-3.5 animate-spin mx-auto" />
                    ) : (
                      st === 'InProgress' ? 'In Progress' : st
                    )}
                  </button>
                ))}
              </div>
            </div>

            {/* Priority Control */}
            <div className="p-4 rounded-xl bg-slate-800/40 border border-slate-800 space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold text-slate-300 uppercase tracking-wider">Target Priority</span>
                <span className="text-[11px] text-slate-400">Current: {ticket.priority}</span>
              </div>
              <div className="grid grid-cols-4 gap-2">
                {(['Low', 'Medium', 'High', 'Critical'] as TicketPriority[]).map((pr) => (
                  <button
                    key={pr}
                    type="button"
                    onClick={() => handleUpdateTicket(statusDraft, pr)}
                    disabled={isUpdatingTicket || ticket.priority === pr}
                    className={`px-2 py-2 rounded-xl text-xs font-semibold transition border ${
                      ticket.priority === pr
                        ? 'bg-indigo-600/20 border-indigo-500/50 text-indigo-300 cursor-default'
                        : 'bg-slate-800 hover:bg-slate-700/80 border-slate-700 text-slate-300'
                    } disabled:opacity-50`}
                  >
                    {pr}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Navigation Tabs */}
      <div className="flex border-b border-slate-800 gap-6 text-sm font-semibold">
        <button
          onClick={() => setActiveTab('conversation')}
          className={`pb-3 flex items-center gap-2 border-b-2 transition ${
            activeTab === 'conversation'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <MessageSquare className="w-4 h-4" />
          <span>Comments & Conversation ({comments.length})</span>
        </button>

        <button
          onClick={() => setActiveTab('history')}
          className={`pb-3 flex items-center gap-2 border-b-2 transition ${
            activeTab === 'history'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <History className="w-4 h-4" />
          <span>Audit History ({history.length})</span>
        </button>

        <button
          onClick={() => setActiveTab('ai')}
          className={`pb-3 flex items-center gap-2 border-b-2 transition ${
            activeTab === 'ai'
              ? 'border-indigo-500 text-indigo-400'
              : 'border-transparent text-slate-400 hover:text-slate-200'
          }`}
        >
          <Sparkles className="w-4 h-4 text-indigo-400" />
          <span>AI Troubleshooting Assistant</span>
        </button>
      </div>

      {/* Tab 1: Conversation & Comments */}
      {activeTab === 'conversation' && (
        <div className="space-y-6">
          {/* Comments List */}
          <div className="space-y-3">
            {comments.length === 0 ? (
              <div className="p-8 rounded-2xl bg-slate-900 border border-slate-800 text-center text-slate-400 text-sm">
                <MessageSquare className="w-8 h-8 text-slate-600 mx-auto mb-2" />
                <p>No comments on this ticket yet. Add a response below to communicate with the ticket requestor.</p>
              </div>
            ) : (
              comments.map((comment) => (
                <div
                  key={comment.id}
                  className="bg-slate-900 p-5 rounded-2xl border border-slate-800 space-y-2 shadow-sm"
                >
                  <div className="flex items-center justify-between text-xs">
                    <div className="flex items-center gap-2 font-semibold text-slate-200">
                      <div className="w-6 h-6 rounded-full bg-slate-800 border border-slate-700 flex items-center justify-center text-[10px] font-bold text-indigo-300">
                        {comment.author ? comment.author.charAt(0).toUpperCase() : 'U'}
                      </div>
                      <span>{comment.author || `User #${comment.userId}`}</span>
                    </div>
                    <span className="text-slate-400 font-mono">
                      {new Date(comment.createdAt).toLocaleString([], {
                        month: 'short',
                        day: 'numeric',
                        hour: '2-digit',
                        minute: '2-digit',
                      })}
                    </span>
                  </div>
                  <div className="text-sm text-slate-300 whitespace-pre-wrap pl-8">
                    {comment.content}
                  </div>
                </div>
              ))
            )}
          </div>

          {/* Add Comment Box */}
          <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm space-y-4">
            <h3 className="text-sm font-semibold text-white flex items-center gap-2">
              <MessageSquare className="w-4 h-4 text-indigo-400" />
              <span>{isAgentOrAdmin ? 'Post Support Response' : 'Add a Comment to Ticket'}</span>
            </h3>

            {commentError && (
              <div className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{commentError}</span>
              </div>
            )}

            <form onSubmit={handleAddComment} className="space-y-3">
              <textarea
                rows={4}
                required
                value={newComment}
                onChange={(e) => setNewComment(e.target.value)}
                placeholder={
                  isAgentOrAdmin
                    ? 'Write your official support response to the employee. (Submitting this automatically records the SLA First Response milestone if pending)...'
                    : 'Type additional details, update information, or reply to support agents...'
                }
                className="w-full px-4 py-3 bg-slate-800/80 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 resize-y"
                disabled={isSubmittingComment}
              />

              <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 pt-2">
                <span className="text-xs text-slate-400">
                  {isAgentOrAdmin
                    ? 'Your comment will be immediately visible to the requester and logged in the ticket audit history.'
                    : 'Comments are visible to assigned support personnel and ResolveIQ administrators.'}
                </span>
                <button
                  type="submit"
                  disabled={isSubmittingComment || !newComment.trim()}
                  className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-xl text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed transition shrink-0"
                >
                  {isSubmittingComment ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Posting...</span>
                    </>
                  ) : (
                    <>
                      <Send className="w-3.5 h-3.5" />
                      <span>{isAgentOrAdmin ? 'Submit Response' : 'Post Comment'}</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Tab 2: Audit History Timeline */}
      {activeTab === 'history' && (
        <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm space-y-4">
          <h3 className="text-sm font-semibold text-white flex items-center gap-2">
            <History className="w-4 h-4 text-indigo-400" />
            <span>Audit Trail & Lifecycle Events</span>
          </h3>

          {history.length === 0 ? (
            <p className="text-slate-400 text-sm py-8 text-center">
              No audit log entries recorded for this ticket yet.
            </p>
          ) : (
            <div className="relative border-l border-slate-800 ml-4 space-y-6 py-2">
              {history.map((event) => (
                <div key={event.id} className="relative pl-6">
                  <div className="absolute -left-2 top-1.5 w-4 h-4 rounded-full bg-slate-900 border-2 border-indigo-500"></div>
                  <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-1 text-xs">
                    <span className="font-semibold text-slate-200">
                      {event.action}{' '}
                      <span className="text-slate-400 font-normal">
                        by {event.actorName} ({event.actorRole})
                      </span>
                    </span>
                    <span className="text-slate-400 font-mono">
                      {new Date(event.createdAt).toLocaleString([], {
                        month: 'short',
                        day: 'numeric',
                        hour: '2-digit',
                        minute: '2-digit',
                      })}
                    </span>
                  </div>
                  {(event.oldValue || event.newValue) && (
                    <div className="mt-1 text-xs text-slate-400 bg-slate-800/40 p-2.5 rounded-xl border border-slate-800/60 font-mono">
                      {event.oldValue && <div>Previous: {event.oldValue}</div>}
                      {event.newValue && <div className="text-slate-300">Updated: {event.newValue}</div>}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Tab 3: AI Intelligence Engine (Phase 5) */}
      {activeTab === 'ai' && (
        <div className="space-y-6">
          {/* Card 1: AI Priority Advisor */}
          <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
              <div>
                <div className="flex items-center gap-2">
                  <Award className="w-5 h-5 text-indigo-400" />
                  <h3 className="text-base font-bold text-white">AI Priority Advisor</h3>
                </div>
                <p className="text-xs text-slate-400 mt-1 max-w-xl">
                  Evaluates ticket title, description, and enterprise context to recommend SLA priority targets. Inferences are strictly advisory and never silently mutate ticket data.
                </p>
              </div>

              <button
                type="button"
                onClick={handleRunPriorityRecommendation}
                disabled={isRecommendingPriority}
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-500 shadow-md shadow-indigo-600/20 disabled:opacity-50 transition shrink-0"
              >
                {isRecommendingPriority ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Analyzing Priority...</span>
                  </>
                ) : (
                  <>
                    <Zap className="w-4 h-4" />
                    <span>Evaluate Priority</span>
                  </>
                )}
              </button>
            </div>

            {priorityRecError && (
              <div className="p-4 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-300 text-xs flex items-start gap-3">
                <HelpCircle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                <div className="flex-1">
                  <p className="font-semibold">{priorityRecError}</p>
                  <p className="mt-1 text-amber-300/80">
                    Normal ticket operations and manual priority controls operate completely independently of external AI services.
                  </p>
                </div>
              </div>
            )}

            {priorityRec && (
              <div className="p-5 rounded-xl bg-slate-800/40 border border-slate-700/60 space-y-4 animate-fade-in">
                <div className="flex flex-wrap items-center justify-between gap-3 pb-3 border-b border-slate-700/50">
                  <div className="flex items-center gap-3">
                    <span className="text-xs text-slate-400 font-semibold uppercase tracking-wider">Current:</span>
                    <PriorityBadge priority={ticket.priority} />
                    <span className="text-slate-500">→</span>
                    <span className="text-xs text-slate-400 font-semibold uppercase tracking-wider">AI Recommended:</span>
                    <PriorityBadge priority={priorityRec.recommendedPriority} />
                  </div>

                  {priorityRec.confidenceScore != null && (
                    <div className="text-xs text-slate-400 font-mono">
                      Confidence: <strong className="text-indigo-400 font-bold">{Math.round(priorityRec.confidenceScore * 100)}%</strong>
                    </div>
                  )}
                </div>

                <div className="text-xs space-y-2">
                  <span className="text-slate-400 uppercase tracking-wider block font-semibold">AI Rationale:</span>
                  <p className="text-slate-200 leading-relaxed bg-slate-950/40 p-3 rounded-lg border border-slate-800">
                    {priorityRec.rationale}
                  </p>
                </div>

                {priorityRec.uncertaintyOrMissingInfo && (
                  <div className="text-xs text-amber-300/90 bg-amber-500/10 p-2.5 rounded-lg border border-amber-500/20">
                    <strong>Caveat / Missing Information:</strong> {priorityRec.uncertaintyOrMissingInfo}
                  </div>
                )}

                {/* Explicit Action to adopt AI recommendation (Zero silent mutation) */}
                {isAgentOrAdmin && ticket.priority !== priorityRec.recommendedPriority && (
                  <div className="pt-2 flex items-center justify-between gap-3 border-t border-slate-800">
                    <span className="text-[11px] text-slate-400 italic">
                      AI is advisory only. Priority is unchanged until you explicitly apply this recommendation.
                    </span>
                    <button
                      type="button"
                      onClick={() => handleUpdateTicket(statusDraft, priorityRec.recommendedPriority)}
                      disabled={isUpdatingTicket}
                      className="inline-flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold text-white bg-indigo-600 hover:bg-indigo-500 transition shrink-0"
                    >
                      <Check className="w-3.5 h-3.5" />
                      <span>Apply {priorityRec.recommendedPriority} Priority</span>
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Card 2: AI Troubleshooting Assistant */}
          <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 shadow-sm space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
              <div>
                <div className="flex items-center gap-2">
                  <Sparkles className="w-5 h-5 text-indigo-400" />
                  <h3 className="text-base font-bold text-white">ITSM Troubleshooting Assistant</h3>
                </div>
                <p className="text-xs text-slate-400 mt-1 max-w-xl">
                  Analyze this ticket against ResolveIQ Knowledge Base articles and historical resolved cases to generate hypotheses and technical troubleshooting steps.
                </p>
              </div>

              <button
                type="button"
                onClick={handleRunAiTroubleshooting}
                disabled={isTroubleshooting}
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-xs font-semibold text-white bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 shadow-md shadow-indigo-600/30 disabled:opacity-50 transition shrink-0"
              >
                {isTroubleshooting ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Analyzing Ticket...</span>
                  </>
                ) : (
                  <>
                    <Sparkles className="w-4 h-4" />
                    <span>Generate AI Advice</span>
                  </>
                )}
              </button>
            </div>

            {/* AI Error Alert */}
            {aiError && (
              <div className="p-4 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-300 text-xs flex items-start gap-3">
                <HelpCircle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
                <div className="flex-1">
                  <p className="font-semibold">{aiError}</p>
                  <p className="mt-1 text-amber-300/80">
                    Normal ticket workflows, comments, and agent SLA monitoring operate completely independently of external AI services.
                  </p>
                </div>
              </div>
            )}
          </div>

          {/* AI Troubleshooting Result Card */}
          {aiResult && (
            <div className="bg-slate-900 p-6 rounded-2xl border border-indigo-500/30 shadow-md space-y-6">
              {/* Grounding & Header */}
              <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 pb-4 border-b border-slate-800">
                <div className="flex items-center gap-2">
                  <span className="w-2.5 h-2.5 rounded-full bg-emerald-400 animate-pulse"></span>
                  <span className="text-xs font-semibold text-indigo-300 uppercase tracking-wider">
                    AI Troubleshooting Inferences
                  </span>
                </div>
                {aiResult.groundingNotice && (
                  <span className="text-[11px] text-slate-400 italic">
                    {aiResult.groundingNotice}
                  </span>
                )}
              </div>

              {/* Summary */}
              {aiResult.summary && (
                <div>
                  <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1">
                    Problem Summary
                  </h4>
                  <p className="text-sm text-slate-200 leading-relaxed bg-slate-950/40 p-3.5 rounded-xl border border-slate-800">
                    {aiResult.summary}
                  </p>
                </div>
              )}

              {/* Hypothesized Causes */}
              {aiResult.hypothesizedCauses && aiResult.hypothesizedCauses.length > 0 && (
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
                      <span>Hypothesized Root Causes</span>
                    </h4>
                    <span className="text-[10px] text-amber-400 font-medium bg-amber-500/10 px-2 py-0.5 rounded border border-amber-500/20">
                      Hypotheses Only — Verify in Environment
                    </span>
                  </div>
                  <ul className="space-y-2 text-xs text-slate-200">
                    {aiResult.hypothesizedCauses.map((cause, idx) => (
                      <li
                        key={idx}
                        className="flex items-start gap-2.5 p-3 rounded-xl bg-slate-800/40 border border-slate-800"
                      >
                        <span className="w-5 h-5 rounded-md bg-amber-500/10 text-amber-400 font-bold flex items-center justify-center shrink-0 text-[10px]">
                          ?
                        </span>
                        <span className="leading-relaxed">{cause}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Troubleshooting Steps */}
              {aiResult.troubleshootingSteps && aiResult.troubleshootingSteps.length > 0 && (
                <div>
                  <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
                    Recommended Self-Service Steps
                  </h4>
                  <ol className="space-y-2 text-xs text-slate-200">
                    {aiResult.troubleshootingSteps.map((step, idx) => (
                      <li
                        key={idx}
                        className="flex items-start gap-2.5 p-3 rounded-xl bg-slate-800/40 border border-slate-800"
                      >
                        <span className="w-5 h-5 rounded-md bg-indigo-500/20 text-indigo-400 font-bold flex items-center justify-center shrink-0 text-[10px]">
                          {idx + 1}
                        </span>
                        <span className="leading-relaxed">{step}</span>
                      </li>
                    ))}
                  </ol>
                </div>
              )}

              {/* Clarifying Questions */}
              {aiResult.clarifyingQuestions && aiResult.clarifyingQuestions.length > 0 && (
                <div>
                  <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2">
                    Questions for Further Investigation
                  </h4>
                  <ul className="space-y-1.5 text-xs text-slate-300">
                    {aiResult.clarifyingQuestions.map((q, idx) => (
                      <li key={idx} className="flex items-start gap-2 pl-2">
                        <span className="text-indigo-400 font-bold">•</span>
                        <span>{q}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Grounded Knowledge Articles & Similar Tickets */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-4 border-t border-slate-800">
                {/* Knowledge Articles */}
                <div>
                  <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2 flex items-center gap-1.5">
                    <BookOpen className="w-3.5 h-3.5 text-indigo-400" />
                    <span>Relevant Knowledge Articles</span>
                  </h4>
                  {aiResult.relevantKnowledgeArticles && aiResult.relevantKnowledgeArticles.length > 0 ? (
                    <div className="space-y-2">
                      {aiResult.relevantKnowledgeArticles.map((art) => (
                        <div
                          key={art.id}
                          className="p-3 rounded-xl bg-slate-800/50 border border-slate-700/60 text-xs space-y-1"
                        >
                          <div className="font-semibold text-slate-200 flex items-center justify-between">
                            <span>{art.title}</span>
                            <span className="text-[10px] text-indigo-300 bg-indigo-500/10 px-1.5 py-0.5 rounded">
                              {art.category}
                            </span>
                          </div>
                          <p className="text-slate-400 line-clamp-2">{art.excerpt}</p>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="text-xs text-slate-400 italic">No specific knowledge articles matched.</p>
                  )}
                </div>

                {/* Similar Resolved Tickets */}
                <div>
                  <h4 className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-2 flex items-center gap-1.5">
                    <CheckSquare className="w-3.5 h-3.5 text-emerald-400" />
                    <span>Similar Resolved Cases</span>
                  </h4>
                  {aiResult.similarResolvedTickets && aiResult.similarResolvedTickets.length > 0 ? (
                    <div className="space-y-2">
                      {aiResult.similarResolvedTickets.map((t) => (
                        <div
                          key={t.ticketId}
                          className="p-3 rounded-xl bg-slate-800/50 border border-slate-700/60 text-xs space-y-1"
                        >
                          <div className="font-semibold text-slate-200 flex items-center justify-between">
                            <span>Ticket #{t.ticketId}: {t.title}</span>
                            <PriorityBadge priority={t.priority} />
                          </div>
                          {t.resolvedAt && (
                            <div className="text-[10px] text-slate-400">
                              Resolved: {new Date(t.resolvedAt).toLocaleDateString()}
                            </div>
                          )}
                          <p className="text-slate-400 line-clamp-2">{t.excerpt}</p>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="text-xs text-slate-400 italic">No past resolved cases matched caller permissions.</p>
                  )}
                </div>
              </div>

              {/* Safety Notice */}
              <div className="p-3 rounded-xl bg-slate-800/30 border border-slate-800 text-[11px] text-slate-400 text-center">
                ResolveIQ AI generates recommendations for informational purposes only. It never silently alters ticket priorities, assignments, or SLA deadlines.
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
