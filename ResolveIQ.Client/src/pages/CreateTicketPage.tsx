import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { ticketsApi, aiApi, ApiClientError } from '../services/api';
import { TicketPriority, TicketClassificationResult } from '../types';
import { PriorityBadge } from '../components/Badge';
import {
  PlusCircle,
  Sparkles,
  ArrowLeft,
  AlertCircle,
  CheckCircle2,
  HelpCircle,
  Tag,
  Clock,
  Send,
  Loader2,
} from 'lucide-react';

const PRIORITY_SLA_INFO: Record<TicketPriority, { response: string; resolution: string; desc: string }> = {
  Critical: {
    response: '15 minutes',
    resolution: '4 hours',
    desc: 'Severe production outages or complete service interruptions.',
  },
  High: {
    response: '1 hour',
    resolution: '8 hours',
    desc: 'Major business function impaired with no workaround.',
  },
  Medium: {
    response: '4 hours',
    resolution: '24 hours',
    desc: 'Normal issue or inquiry with existing temporary workaround.',
  },
  Low: {
    response: '8 hours',
    resolution: '72 hours',
    desc: 'Minor cosmetic glitch, general question, or routine request.',
  },
};

export const CreateTicketPage: React.FC = () => {
  const navigate = useNavigate();

  // Form State
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<TicketPriority>('Medium');

  // Submission State
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  // AI Classification State
  const [isClassifying, setIsClassifying] = useState(false);
  const [aiResult, setAiResult] = useState<TicketClassificationResult | null>(null);
  const [aiError, setAiError] = useState<string | null>(null);
  const [aiAppliedPriority, setAiAppliedPriority] = useState<boolean>(false);

  const handleAnalyzeWithAi = async () => {
    if (!title.trim() || !description.trim()) {
      setAiError('Please provide both a title and description before requesting AI classification.');
      return;
    }

    if (title.length > 200) {
      setAiError('Title cannot exceed 200 characters for AI analysis.');
      return;
    }

    setIsClassifying(true);
    setAiError(null);
    setAiAppliedPriority(false);

    try {
      const result = await aiApi.classifyTicket({
        title: title.trim(),
        description: description.trim(),
      });
      setAiResult(result);
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        if (err.isRateLimited) {
          setAiError('AI rate limit reached. Please wait a moment before trying again.');
        } else if (err.status === 503) {
          setAiError('AI Classification Engine is currently offline or unconfigured on the server.');
        } else if (err.status === 504) {
          setAiError('AI classification request timed out. You may continue submitting manually.');
        } else {
          setAiError(err.message || 'AI service returned an unexpected error.');
        }
      } else {
        setAiError(err instanceof Error ? err.message : 'Failed to consult AI service.');
      }
    } finally {
      setIsClassifying(false);
    }
  };

  const handleApplyAiPriority = () => {
    if (aiResult?.suggestedPriority) {
      setPriority(aiResult.suggestedPriority);
      setAiAppliedPriority(true);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitError(null);
    setFieldErrors({});

    const trimmedTitle = title.trim();
    const trimmedDesc = description.trim();

    if (!trimmedTitle) {
      setSubmitError('Ticket title is required.');
      return;
    }
    if (trimmedTitle.length > 150) {
      setSubmitError('Title cannot exceed 150 characters.');
      return;
    }
    if (!trimmedDesc) {
      setSubmitError('Ticket description is required.');
      return;
    }

    setIsSubmitting(true);

    try {
      const created = await ticketsApi.create({
        title: trimmedTitle,
        description: trimmedDesc,
        priority,
      });

      // Navigate to the newly created ticket details page
      navigate(`/employee/tickets/${created.id}`, {
        state: { message: `Ticket #${created.id} was created successfully!` },
      });
    } catch (err: unknown) {
      if (err instanceof ApiClientError) {
        setSubmitError(err.message || 'Failed to create ticket.');
        if (err.errors) {
          setFieldErrors(err.errors);
        }
      } else {
        setSubmitError(err instanceof Error ? err.message : 'Failed to submit ticket. Please retry.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      {/* Breadcrumb & Navigation */}
      <div className="flex items-center justify-between">
        <Link
          to="/employee/tickets"
          className="inline-flex items-center gap-2 text-sm text-slate-400 hover:text-slate-200 transition"
        >
          <ArrowLeft className="w-4 h-4" />
          <span>Back to My Tickets</span>
        </Link>
        <span className="text-xs text-slate-400">ResolveIQ Employee Portal</span>
      </div>

      {/* Header Banner */}
      <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-indigo-500/10 border border-indigo-500/20 flex items-center justify-center text-indigo-400">
            <PlusCircle className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-white">Submit New Support Ticket</h1>
            <p className="text-sm text-slate-400 mt-0.5">
              Provide issue details below. Our support agents will triage your request according to SLA deadlines.
            </p>
          </div>
        </div>
      </div>

      {/* Error Alert */}
      {submitError && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-sm flex items-start gap-3">
          <AlertCircle className="w-5 h-5 text-rose-400 shrink-0 mt-0.5" />
          <div className="flex-1">
            <p className="font-semibold">{submitError}</p>
            {fieldErrors && Object.keys(fieldErrors).length > 0 && (
              <ul className="mt-2 list-disc list-inside text-xs space-y-1 text-rose-300/80">
                {Object.entries(fieldErrors).map(([field, msgs]) => (
                  <li key={field}>
                    <span className="font-mono">{field}:</span> {msgs.join(', ')}
                  </li>
                ))}
              </ul>
            )}
            <p className="text-xs mt-2 text-rose-300/70">
              Your draft has been preserved. Please adjust the input and retry submission.
            </p>
          </div>
        </div>
      )}

      {/* Main Ticket Form Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left 2 Columns: Form Fields */}
        <form onSubmit={handleSubmit} className="lg:col-span-2 space-y-5">
          <div className="bg-slate-900 p-6 rounded-2xl border border-slate-800 space-y-5 shadow-sm">
            {/* Title */}
            <div>
              <div className="flex items-center justify-between mb-2">
                <label htmlFor="ticket-title" className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
                  Subject / Summary <span className="text-rose-400">*</span>
                </label>
                <span className="text-xs text-slate-400 font-mono">
                  {title.length}/150
                </span>
              </div>
              <input
                id="ticket-title"
                type="text"
                required
                maxLength={150}
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="e.g., Cannot access VPN gateway from remote office"
                className="w-full px-4 py-3 bg-slate-800/80 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 transition"
                disabled={isSubmitting}
              />
            </div>

            {/* Description */}
            <div>
              <div className="flex items-center justify-between mb-2">
                <label htmlFor="ticket-description" className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
                  Detailed Description <span className="text-rose-400">*</span>
                </label>
                <span className="text-xs text-slate-400 font-mono">
                  {description.length}/4000
                </span>
              </div>
              <textarea
                id="ticket-description"
                required
                rows={7}
                maxLength={4000}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Describe what happened, any error messages displayed, troubleshooting steps already attempted, and how this impacts your work..."
                className="w-full px-4 py-3 bg-slate-800/80 border border-slate-700 rounded-xl text-sm text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 transition resize-y"
                disabled={isSubmitting}
              />
            </div>

            {/* Priority Selector */}
            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
                  Priority Level
                </label>
                <span className="text-xs text-slate-400">
                  Default: <span className="text-slate-300 font-medium">Medium</span>
                </span>
              </div>
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5">
                {(['Low', 'Medium', 'High', 'Critical'] as TicketPriority[]).map((p) => {
                  const isSelected = priority === p;
                  return (
                    <button
                      type="button"
                      key={p}
                      onClick={() => setPriority(p)}
                      disabled={isSubmitting}
                      className={`p-3 rounded-xl border text-left transition-all ${
                        isSelected
                          ? 'border-indigo-500 bg-indigo-500/15 ring-2 ring-indigo-500/30'
                          : 'border-slate-700/80 bg-slate-800/40 hover:bg-slate-800 text-slate-400'
                      }`}
                    >
                      <div className="flex items-center justify-between">
                        <span className={`text-xs font-bold ${isSelected ? 'text-white' : 'text-slate-300'}`}>
                          {p}
                        </span>
                        {isSelected && <CheckCircle2 className="w-3.5 h-3.5 text-indigo-400" />}
                      </div>
                      <div className="mt-1 text-[11px] text-slate-400">
                        SLA: {PRIORITY_SLA_INFO[p].resolution}
                      </div>
                    </button>
                  );
                })}
              </div>
              <p className="mt-2 text-xs text-slate-400">
                {PRIORITY_SLA_INFO[priority].desc}
              </p>
            </div>

            {/* Submit Action Bar */}
            <div className="pt-4 border-t border-slate-800 flex flex-col sm:flex-row items-center justify-between gap-3">
              <Link
                to="/employee/tickets"
                className="w-full sm:w-auto px-4 py-2.5 rounded-xl border border-slate-700 text-slate-300 hover:text-white hover:bg-slate-800 text-xs font-semibold text-center transition"
              >
                Cancel
              </Link>
              <button
                type="submit"
                disabled={isSubmitting}
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-6 py-2.5 rounded-xl text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-500 shadow-md shadow-indigo-600/30 disabled:opacity-50 disabled:cursor-not-allowed transition"
              >
                {isSubmitting ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Submitting Ticket...</span>
                  </>
                ) : (
                  <>
                    <Send className="w-4 h-4" />
                    <span>Submit Support Ticket</span>
                  </>
                )}
              </button>
            </div>
          </div>
        </form>

        {/* Right 1 Column: AI Intelligence Assistant & SLA Targets */}
        <div className="space-y-6">
          {/* AI Intelligence Assistant */}
          <div className="bg-slate-900 p-5 rounded-2xl border border-slate-800 shadow-sm space-y-4">
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 rounded-lg bg-indigo-500/10 border border-indigo-500/20 flex items-center justify-center text-indigo-400">
                <Sparkles className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-sm font-semibold text-white">AI Classification Assistant</h3>
                <p className="text-[11px] text-slate-400">Powered by Gemini & ResolveIQ Intelligence</p>
              </div>
            </div>

            <p className="text-xs text-slate-300 leading-relaxed">
              Let the AI model review your subject and description to recommend a ticket category and suggest an appropriate priority level.
            </p>

            <button
              type="button"
              onClick={handleAnalyzeWithAi}
              disabled={isClassifying || !title.trim() || !description.trim()}
              className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs font-semibold shadow-md shadow-indigo-600/20 disabled:opacity-40 disabled:cursor-not-allowed transition"
            >
              {isClassifying ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Analyzing Ticket...</span>
                </>
              ) : (
                <>
                  <Sparkles className="w-3.5 h-3.5" />
                  <span>Analyze with AI</span>
                </>
              )}
            </button>

            {aiError && (
              <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/20 text-amber-300 text-xs flex items-start gap-2">
                <HelpCircle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
                <span>{aiError}</span>
              </div>
            )}

            {aiResult && (
              <div className="p-4 rounded-xl bg-indigo-950/40 border border-indigo-500/30 space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold text-indigo-300 uppercase tracking-wider">
                    AI Suggestions
                  </span>
                  <span className="text-[11px] text-slate-400">
                    Confidence: {aiResult.confidenceScore !== null && aiResult.confidenceScore !== undefined ? `${Math.round(aiResult.confidenceScore * 100)}%` : 'N/A'}
                  </span>
                </div>

                <div className="space-y-1.5 text-xs">
                  <div className="flex items-center gap-2">
                    <span className="text-slate-400">Category:</span>
                    <span className="text-slate-100 font-medium">
                      {aiResult.category} {aiResult.subcategory ? `› ${aiResult.subcategory}` : ''}
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-slate-400">Suggested Priority:</span>
                    <PriorityBadge priority={aiResult.suggestedPriority} />
                  </div>
                </div>

                {aiResult.rationale && (
                  <p className="text-[11px] text-slate-300 italic border-l-2 border-indigo-500/50 pl-2">
                    "{aiResult.rationale}"
                  </p>
                )}

                {aiResult.suggestedTags && aiResult.suggestedTags.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 pt-1">
                    {aiResult.suggestedTags.map((tag) => (
                      <span
                        key={tag}
                        className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] bg-slate-800 text-slate-300 border border-slate-700"
                      >
                        <Tag className="w-2.5 h-2.5 text-slate-400" />
                        {tag}
                      </span>
                    ))}
                  </div>
                )}

                <div className="pt-2 border-t border-indigo-500/20 flex flex-col gap-2">
                  <button
                    type="button"
                    onClick={handleApplyAiPriority}
                    disabled={priority === aiResult.suggestedPriority}
                    className="w-full py-1.5 px-3 rounded-lg bg-indigo-600/30 hover:bg-indigo-600/50 border border-indigo-500/40 text-indigo-200 text-xs font-semibold disabled:opacity-50 transition"
                  >
                    {aiAppliedPriority || priority === aiResult.suggestedPriority
                      ? '✓ Priority Applied'
                      : `Apply Priority: ${aiResult.suggestedPriority}`}
                  </button>
                  <p className="text-[10px] text-slate-400 text-center">
                    {aiResult.confidenceCalibrationNote || 'Model-reported heuristic confidence; not statistically calibrated.'}
                  </p>
                </div>
              </div>
            )}
          </div>

          {/* SLA Target Reference Box */}
          <div className="bg-slate-900 p-5 rounded-2xl border border-slate-800 shadow-sm space-y-3">
            <div className="flex items-center gap-2">
              <Clock className="w-4 h-4 text-slate-400" />
              <h3 className="text-xs font-semibold text-slate-300 uppercase tracking-wider">
                Service Level Agreement (SLA)
              </h3>
            </div>
            <div className="space-y-2 text-xs">
              <div className="flex justify-between py-1 border-b border-slate-800">
                <span className="text-slate-400">Critical Priority:</span>
                <span className="text-rose-400 font-mono font-medium">15m / 4h</span>
              </div>
              <div className="flex justify-between py-1 border-b border-slate-800">
                <span className="text-slate-400">High Priority:</span>
                <span className="text-amber-400 font-mono font-medium">1h / 8h</span>
              </div>
              <div className="flex justify-between py-1 border-b border-slate-800">
                <span className="text-slate-400">Medium Priority:</span>
                <span className="text-indigo-400 font-mono font-medium">4h / 24h</span>
              </div>
              <div className="flex justify-between py-1">
                <span className="text-slate-400">Low Priority:</span>
                <span className="text-slate-300 font-mono font-medium">8h / 72h</span>
              </div>
            </div>
            <p className="text-[11px] text-slate-400 pt-1">
              Deadlines are automatically calculated upon ticket creation and monitored in real time.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};
