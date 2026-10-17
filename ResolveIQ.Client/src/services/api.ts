import {
  AuthResponse,
  Ticket,
  TicketDashboardResponse,
  PagedResult,
  TicketQueryParameters,
  CreateTicketRequest,
  UpdateTicketRequest,
  TicketComment,
  CreateCommentRequest,
  TicketHistory,
  TicketSlaStatus,
  ClassifyTicketRequest,
  TicketClassificationResult,
  RecommendPriorityRequest,
  PriorityRecommendationResult,
  TroubleshootingResult,
  User,
  ApiError,
  EscalationEvent,
  NotificationItem,
  NotificationQueryParameters,
} from '../types';

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5022').replace(/\/+$/, '');

export class ApiClientError extends Error {
  status: number;
  title?: string;
  errors?: Record<string, string[]>;
  traceId?: string;

  constructor(status: number, message: string, title?: string, errors?: Record<string, string[]>, traceId?: string) {
    super(message);
    this.name = 'ApiClientError';
    this.status = status;
    this.title = title;
    this.errors = errors;
    this.traceId = traceId;
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }

  get isForbidden(): boolean {
    return this.status === 403;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  get isRateLimited(): boolean {
    return this.status === 429;
  }

  get isServerError(): boolean {
    return this.status >= 500;
  }
}

// Token storage key
const TOKEN_KEY = 'resolveiq_auth_token';

export const getStoredToken = (): string | null => {
  return sessionStorage.getItem(TOKEN_KEY);
};

export const setStoredToken = (token: string | null): void => {
  if (token) {
    sessionStorage.setItem(TOKEN_KEY, token);
  } else {
    sessionStorage.removeItem(TOKEN_KEY);
  }
};

interface RequestOptions extends RequestInit {
  requiresAuth?: boolean;
}

export async function request<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
  const { requiresAuth = true, headers: customHeaders = {}, ...rest } = options;

  const url = `${API_BASE_URL}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

  const headers = new Headers(customHeaders);
  if (!headers.has('Content-Type') && !(rest.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  if (requiresAuth) {
    const token = getStoredToken();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }
  }

  let response: Response;
  try {
    response = await fetch(url, {
      ...rest,
      headers,
    });
  } catch (err: unknown) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw err;
    }
    const errorMsg = err instanceof Error ? err.message : 'Network transport error.';
    throw new ApiClientError(0, `Unable to connect to the ResolveIQ API server at ${API_BASE_URL}. (${errorMsg})`);
  }

  if (!response.ok) {
    let errorData: ApiError = { message: `Request failed with status ${response.status}` };
    try {
      const parsed = await response.json();
      if (parsed) {
        errorData = {
          message: parsed.detail || parsed.message || (parsed.title ? `${parsed.title}: ${parsed.detail || ''}` : response.statusText),
          title: parsed.title,
          status: response.status,
          errors: parsed.errors,
          traceId: parsed.traceId || parsed.extensions?.traceId,
        };
      }
    } catch {
      // Body not JSON
      errorData.message = response.statusText || `HTTP ${response.status}`;
    }

    // Friendly messages for standard HTTP codes
    if (response.status === 401) {
      errorData.message = errorData.message || 'Authentication required or session expired.';
    } else if (response.status === 403) {
      errorData.message = errorData.message || 'You do not have permission to perform this action.';
    } else if (response.status === 429) {
      errorData.message = errorData.message || 'Rate limit exceeded. Please wait a moment before trying again.';
    } else if (response.status >= 500) {
      errorData.message = errorData.message || 'Server encountered an internal error. Please try again later.';
    }

    throw new ApiClientError(
      response.status,
      errorData.message,
      errorData.title,
      errorData.errors,
      errorData.traceId
    );
  }

  if (response.status === 204) {
    return {} as T;
  }

  return response.json() as Promise<T>;
}

// =============================================================================
// API Service Methods
// =============================================================================

export const authApi = {
  login: (data: { email: string; password: string }, signal?: AbortSignal) =>
    request<AuthResponse>('/api/Auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
      requiresAuth: false,
      signal,
    }),

  register: (data: { fullName: string; email: string; password: string }, signal?: AbortSignal) =>
    request<AuthResponse>('/api/Auth/register', {
      method: 'POST',
      body: JSON.stringify(data),
      requiresAuth: false,
      signal,
    }),

  changePassword: (data: { currentPassword: string; newPassword: string; confirmNewPassword: string }, signal?: AbortSignal) =>
    request<{ message: string }>('/api/Auth/change-password', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    }),
};

export const usersApi = {
  getMe: (signal?: AbortSignal) =>
    request<User>('/api/Users/me', { signal }),

  updateMe: (data: { fullName: string; email: string }, signal?: AbortSignal) =>
    request<User>('/api/Users/me', {
      method: 'PUT',
      body: JSON.stringify(data),
      signal,
    }),

  getAll: (signal?: AbortSignal) =>
    request<User[]>('/api/Users', { signal }),

  updateRole: (id: number, role: 'Employee' | 'Agent', signal?: AbortSignal) =>
    request<{ message: string; user: User }>(`/api/Users/${id}/role`, {
      method: 'PATCH',
      body: JSON.stringify({ role }),
      signal,
    }),
};

export const ticketsApi = {
  getAll: (params: TicketQueryParameters = {}, signal?: AbortSignal) => {
    const query = new URLSearchParams();
    if (params.search) query.set('search', params.search);
    if (params.status) query.set('status', params.status);
    if (params.priority) query.set('priority', params.priority);
    if (params.assigneeId) query.set('assigneeId', params.assigneeId.toString());
    if (params.creatorId) query.set('creatorId', params.creatorId.toString());
    if (params.page) query.set('page', params.page.toString());
    if (params.pageSize) query.set('pageSize', params.pageSize.toString());
    if (params.sortBy) query.set('sortBy', params.sortBy);
    if (params.sortDescending !== undefined) {
      query.set('sortDirection', params.sortDescending ? 'desc' : 'asc');
    }

    const qs = query.toString();
    return request<PagedResult<Ticket>>(`/api/Tickets${qs ? `?${qs}` : ''}`, { signal });
  },

  getAssigned: (params: TicketQueryParameters = {}, signal?: AbortSignal) => {
    const query = new URLSearchParams();
    if (params.search) query.set('search', params.search);
    if (params.status) query.set('status', params.status);
    if (params.priority) query.set('priority', params.priority);
    if (params.page) query.set('page', params.page.toString());
    if (params.pageSize) query.set('pageSize', params.pageSize.toString());
    if (params.sortBy) query.set('sortBy', params.sortBy);
    if (params.sortDescending !== undefined) {
      query.set('sortDirection', params.sortDescending ? 'desc' : 'asc');
    }

    const qs = query.toString();
    return request<PagedResult<Ticket>>(`/api/Tickets/assigned${qs ? `?${qs}` : ''}`, { signal });
  },

  getDashboard: (signal?: AbortSignal) =>
    request<TicketDashboardResponse>('/api/Tickets/dashboard', { signal }),

  getById: (id: number, signal?: AbortSignal) =>
    request<Ticket>(`/api/Tickets/${id}`, { signal }),

  getSla: (ticketId: number, signal?: AbortSignal) =>
    request<TicketSlaStatus>(`/api/Tickets/${ticketId}/sla`, { signal }),

  getHistory: (ticketId: number, signal?: AbortSignal) =>
    request<TicketHistory[]>(`/api/Tickets/${ticketId}/history`, { signal }),

  create: (data: CreateTicketRequest, signal?: AbortSignal) =>
    request<Ticket>('/api/Tickets', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    }),

  update: (id: number, data: UpdateTicketRequest, signal?: AbortSignal) =>
    request<Ticket>(`/api/Tickets/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(data),
      signal,
    }),

  assign: (id: number, agentId: number, signal?: AbortSignal) =>
    request<Ticket>(`/api/Tickets/${id}/assign/${agentId}`, {
      method: 'PATCH',
      signal,
    }),

  getEscalations: (signal?: AbortSignal) =>
    request<EscalationEvent[]>('/api/Tickets/escalations', { signal }),
};

export const commentsApi = {
  getAll: (ticketId: number, signal?: AbortSignal) =>
    request<TicketComment[]>(`/api/tickets/${ticketId}/comments`, { signal }),

  create: (ticketId: number, data: CreateCommentRequest, signal?: AbortSignal) =>
    request<TicketComment>(`/api/tickets/${ticketId}/comments`, {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    }),
};

export const aiApi = {
  classifyTicket: (data: ClassifyTicketRequest, signal?: AbortSignal) =>
    request<TicketClassificationResult>('/api/AI/classify-ticket', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    }),

  recommendPriority: (data: RecommendPriorityRequest, signal?: AbortSignal) =>
    request<PriorityRecommendationResult>('/api/AI/recommend-priority', {
      method: 'POST',
      body: JSON.stringify(data),
      signal,
    }),

  troubleshoot: (ticketId: number, signal?: AbortSignal) =>
    request<TroubleshootingResult>(`/api/AI/troubleshoot/${ticketId}`, {
      method: 'POST',
      signal,
    }),
};

export const healthApi = {
  checkReady: (signal?: AbortSignal) =>
    request<{ status: string; database: string }>('/health/ready', {
      requiresAuth: false,
      signal,
    }),
};

export const notificationsApi = {
  getAll: (params: NotificationQueryParameters = {}, signal?: AbortSignal) => {
    const query = new URLSearchParams();
    if (params.isRead !== undefined) query.set('isRead', params.isRead.toString());
    if (params.page) query.set('page', params.page.toString());
    if (params.pageSize) query.set('pageSize', params.pageSize.toString());
    const qs = query.toString();
    return request<PagedResult<NotificationItem>>(`/api/Notifications${qs ? `?${qs}` : ''}`, { signal });
  },

  getUnreadCount: (signal?: AbortSignal) =>
    request<{ unreadCount: number }>('/api/Notifications/unread-count', { signal }),

  markAsRead: (id: number, signal?: AbortSignal) =>
    request<NotificationItem>(`/api/Notifications/${id}/read`, {
      method: 'PATCH',
      signal,
    }),

  markAllAsRead: (signal?: AbortSignal) =>
    request<{ message: string; updatedCount: number }>('/api/Notifications/read-all', {
      method: 'PATCH',
      signal,
    }),
};

