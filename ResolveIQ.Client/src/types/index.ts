export type Role = 'Employee' | 'Agent' | 'Admin';

export type TicketStatus = 'Open' | 'InProgress' | 'Resolved';

export type TicketPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export interface User {
  id: number;
  fullName: string;
  email: string;
  role: Role;
  createdAt: string;
}

export interface AuthResponse {
  token: string;
  userId: number;
  fullName: string;
  email: string;
  role: Role;
}

export interface Ticket {
  id: number;
  title: string;
  description: string;
  status: TicketStatus;
  priority: TicketPriority;
  createdByUserId: number;
  assignedToUserId?: number | null;
  firstResponseDueAt?: string | null;
  resolutionDueAt?: string | null;
  firstRespondedAt?: string | null;
  resolvedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface TicketDashboardResponse {
  totalTickets?: number;
  openTickets?: number;
  inProgressTickets?: number;
  resolvedTickets?: number;
  atRiskTickets?: number;
  breachedTickets?: number;
  slaChecksMet?: number;
  slaChecksBreached?: number;
  slaCompliancePercentage?: number;
  TotalTickets?: number;
  OpenTickets?: number;
  InProgressTickets?: number;
  ResolvedTickets?: number;
  AtRiskTickets?: number;
  BreachedTickets?: number;
  SlaChecksMet?: number;
  SlaChecksBreached?: number;
  SlaCompliancePercentage?: number;
}

export interface EscalationEvent {
  ticketId: number;
  ticketTitle: string;
  priority: TicketPriority;
  status: TicketStatus;
  assignedToUserId?: number | null;
  assignedToName?: string | null;
  escalatedAt: string;
  reason: string;
}

export interface NotificationItem {
  id: number;
  userId: number;
  type: string;
  title: string;
  message: string;
  ticketId?: number | null;
  createdAt: string;
  isRead: boolean;
  readAt?: string | null;
}

export interface NotificationQueryParameters {
  isRead?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TicketQueryParameters {
  search?: string;
  status?: TicketStatus | '';
  priority?: TicketPriority | '';
  assigneeId?: number;
  creatorId?: number;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

export interface CreateTicketRequest {
  title: string;
  description: string;
  priority: TicketPriority;
}

export interface UpdateTicketRequest {
  status?: TicketStatus;
  priority?: TicketPriority;
}

export interface TicketComment {
  id: number;
  ticketId: number;
  userId: number;
  author: string;
  content: string;
  createdAt: string;
}

export interface CreateCommentRequest {
  content: string;
}

export interface TicketHistory {
  id: number;
  ticketId: number;
  userId?: number | null;
  actorName: string;
  actorRole: string;
  action: string;
  oldValue?: string | null;
  newValue?: string | null;
  createdAt: string;
}

export interface TicketSlaStatus {
  ticketId: number;
  firstResponseDueAt?: string | null;
  firstRespondedAt?: string | null;
  firstResponseStatus: string;
  resolutionDueAt?: string | null;
  resolvedAt?: string | null;
  resolutionStatus: string;
}

export interface ClassifyTicketRequest {
  ticketId?: number;
  title?: string;
  description?: string;
}

export interface TicketClassificationResult {
  category: string;
  subcategory?: string | null;
  suggestedPriority: TicketPriority;
  confidenceScore?: number | null;
  isConfidenceCalibrated: boolean;
  confidenceCalibrationNote?: string | null;
  rationale: string;
  suggestedTags: string[];
}

export interface RecommendPriorityRequest {
  ticketId?: number;
  title?: string;
  description?: string;
  impact?: string;
  urgency?: string;
}

export interface PriorityRecommendationResult {
  currentPriority?: string | null;
  recommendedPriority: TicketPriority;
  confidenceScore?: number | null;
  isConfidenceCalibrated: boolean;
  confidenceCalibrationNote?: string | null;
  rationale: string;
  uncertaintyOrMissingInfo?: string | null;
  slaFirstResponseTarget?: string | null;
  slaResolutionTarget?: string | null;
}

export interface KnowledgeArticleSummary {
  id: number;
  title: string;
  category: string;
  excerpt: string;
  tags?: string | null;
}

export interface SimilarTicketSummary {
  ticketId: number;
  title: string;
  priority: TicketPriority;
  resolvedAt?: string | null;
  excerpt: string;
}

export interface TroubleshootingResult {
  ticketId: number;
  ticketTitle: string;
  summary: string;
  hypothesizedCauses: string[];
  troubleshootingSteps: string[];
  clarifyingQuestions: string[];
  relevantKnowledgeArticles: KnowledgeArticleSummary[];
  similarResolvedTickets: SimilarTicketSummary[];
  groundingNotice: string;
}

export interface ApiError {
  message: string;
  status?: number;
  title?: string;
  errors?: Record<string, string[]>;
  traceId?: string;
}

