import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { TicketDetailPage } from '../pages/TicketDetailPage';
import * as AuthContextModule from '../context/AuthContext';
import { ticketsApi, commentsApi, aiApi, ApiClientError } from '../services/api';
import { Ticket, TicketComment, TicketHistory, TicketSlaStatus, TroubleshootingResult } from '../types';

describe('TicketDetailPage — Details, SLA, Audit, Comments & AI Troubleshooting', () => {
  const mockTicket: Ticket = {
    id: 50,
    title: 'Database connection timeouts',
    description: 'PostgreSQL connection pool exhausted on dev cluster',
    status: 'InProgress',
    priority: 'Critical',
    createdByUserId: 10,
    assignedToUserId: 2,
    createdAt: '2026-10-16T09:00:00Z',
    firstResponseDueAt: '2026-10-16T09:15:00Z',
    resolutionDueAt: '2026-10-16T13:00:00Z',
  };

  const mockSla: TicketSlaStatus = {
    ticketId: 50,
    firstResponseDueAt: '2026-10-16T09:15:00Z',
    firstRespondedAt: '2026-10-16T09:10:00Z',
    firstResponseStatus: 'Met',
    resolutionDueAt: '2026-10-16T13:00:00Z',
    resolvedAt: null,
    resolutionStatus: 'On Track',
  };

  const mockHistory: TicketHistory[] = [
    {
      id: 1,
      ticketId: 50,
      userId: 10,
      actorName: 'Alice Employee',
      actorRole: 'Employee',
      action: 'Created',
      oldValue: null,
      newValue: 'Ticket created with priority Critical',
      createdAt: '2026-10-16T09:00:00Z',
    },
    {
      id: 2,
      ticketId: 50,
      userId: 2,
      actorName: 'Bob Agent',
      actorRole: 'Agent',
      action: 'Assigned',
      oldValue: null,
      newValue: 'Assigned to Bob Agent',
      createdAt: '2026-10-16T09:05:00Z',
    },
  ];

  const mockComments: TicketComment[] = [
    {
      id: 1,
      ticketId: 50,
      userId: 2,
      author: 'Bob Agent',
      content: 'Investigating max_connections setting in postgresql.conf',
      createdAt: '2026-10-16T09:10:00Z',
    },
  ];

  beforeEach(() => {
    vi.restoreAllMocks();

    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: {
        id: 10,
        fullName: 'Alice Employee',
        email: 'alice@resolveiq.com',
        role: 'Employee',
        createdAt: '2026-01-01T00:00:00Z',
      },
      token: 'jwt-token',
      role: 'Employee',
      isAuthenticated: true,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });
  });

  it('renders ticket details, SLA information, and initial comments', async () => {
    vi.spyOn(ticketsApi, 'getById').mockResolvedValueOnce(mockTicket);
    vi.spyOn(ticketsApi, 'getSla').mockResolvedValueOnce(mockSla);
    vi.spyOn(ticketsApi, 'getHistory').mockResolvedValueOnce(mockHistory);
    vi.spyOn(commentsApi, 'getAll').mockResolvedValueOnce(mockComments);

    render(
      <MemoryRouter initialEntries={['/employee/tickets/50']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
      expect(screen.getByText(/PostgreSQL connection pool exhausted/i)).toBeInTheDocument();
      expect(screen.getByText('Agent #2')).toBeInTheDocument();
      expect(screen.getByText('Met')).toBeInTheDocument();
      expect(screen.getByText('On Track')).toBeInTheDocument();
      expect(screen.getByText('Investigating max_connections setting in postgresql.conf')).toBeInTheDocument();
    });
  });

  it('handles 403 Forbidden when employee attempts to view another user ticket', async () => {
    vi.spyOn(ticketsApi, 'getById').mockRejectedValueOnce(
      new ApiClientError(403, 'Forbidden: You do not own this ticket.')
    );

    render(
      <MemoryRouter initialEntries={['/employee/tickets/99']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/Access Restricted/i)).toBeInTheDocument();
      expect(screen.getByText(/employees may only access tickets they have submitted/i)).toBeInTheDocument();
    });
  });

  it('handles 404 Not Found when ticket does not exist', async () => {
    vi.spyOn(ticketsApi, 'getById').mockRejectedValueOnce(
      new ApiClientError(404, 'Ticket not found.')
    );

    render(
      <MemoryRouter initialEntries={['/employee/tickets/404404']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/Ticket Not Found/i)).toBeInTheDocument();
    });
  });

  it('allows employee to add a comment to their ticket', async () => {
    vi.spyOn(ticketsApi, 'getById').mockResolvedValueOnce(mockTicket);
    vi.spyOn(ticketsApi, 'getSla').mockResolvedValueOnce(mockSla);
    vi.spyOn(ticketsApi, 'getHistory').mockResolvedValueOnce(mockHistory);
    vi.spyOn(commentsApi, 'getAll').mockResolvedValueOnce(mockComments);

    const addCommentSpy = vi.spyOn(commentsApi, 'create').mockResolvedValueOnce({
      id: 2,
      ticketId: 50,
      userId: 10,
      author: 'Alice Employee',
      content: 'Added more heap memory to the server pod.',
      createdAt: '2026-10-16T09:20:00Z',
    });

    render(
      <MemoryRouter initialEntries={['/employee/tickets/50']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
    });

    const textarea = screen.getByPlaceholderText(/Type additional details/i);
    fireEvent.change(textarea, { target: { value: 'Added more heap memory to the server pod.' } });

    const postBtn = screen.getByRole('button', { name: /Post Comment/i });
    fireEvent.click(postBtn);

    await waitFor(() => {
      expect(addCommentSpy).toHaveBeenCalledWith(50, {
        content: 'Added more heap memory to the server pod.',
      });
      expect(screen.getByText('Added more heap memory to the server pod.')).toBeInTheDocument();
    });
  });

  it('preserves comment draft in textarea when post fails', async () => {
    vi.spyOn(ticketsApi, 'getById').mockResolvedValueOnce(mockTicket);
    vi.spyOn(ticketsApi, 'getSla').mockResolvedValueOnce(mockSla);
    vi.spyOn(ticketsApi, 'getHistory').mockResolvedValueOnce([]);
    vi.spyOn(commentsApi, 'getAll').mockResolvedValueOnce([]);

    vi.spyOn(commentsApi, 'create').mockRejectedValueOnce(
      new ApiClientError(500, 'Database write timeout.')
    );

    render(
      <MemoryRouter initialEntries={['/employee/tickets/50']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
    });

    const textarea = screen.getByPlaceholderText(/Type additional details/i) as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: 'Draft comment to preserve' } });

    fireEvent.click(screen.getByRole('button', { name: /Post Comment/i }));

    await waitFor(() => {
      expect(screen.getByText(/Database write timeout./i)).toBeInTheDocument();
    });

    expect(textarea.value).toBe('Draft comment to preserve');
  });

  it('runs AI troubleshooting and displays hypotheses without altering ticket fields', async () => {
    vi.spyOn(ticketsApi, 'getById').mockResolvedValueOnce(mockTicket);
    vi.spyOn(ticketsApi, 'getSla').mockResolvedValueOnce(mockSla);
    vi.spyOn(ticketsApi, 'getHistory').mockResolvedValueOnce([]);
    vi.spyOn(commentsApi, 'getAll').mockResolvedValueOnce([]);

    const mockAiTroubleshoot: TroubleshootingResult = {
      ticketId: 50,
      ticketTitle: 'Database connection timeouts',
      summary: 'Connections to PostgreSQL pool are exhausting file descriptors.',
      hypothesizedCauses: ['Connection leak in API background worker', 'max_connections set too low'],
      troubleshootingSteps: ['Check pg_stat_activity', 'Increase pool max size in appsettings.json'],
      clarifyingQuestions: ['Did this begin after the Batch 4 deployment?'],
      relevantKnowledgeArticles: [
        {
          id: 1,
          title: 'PostgreSQL Connection Pooling Guide',
          category: 'Infrastructure',
          excerpt: 'Configuring Npgsql pool sizing for high concurrency workloads.',
        },
      ],
      similarResolvedTickets: [
        {
          ticketId: 12,
          title: 'Staging DB connection spikes',
          priority: 'High',
          resolvedAt: '2026-10-10T12:00:00Z',
          excerpt: 'Fixed by closing idle db contexts in background tasks.',
        },
      ],
      groundingNotice: 'Grounded on ResolveIQ KB and 1 similar resolved case.',
    };

    const aiSpy = vi.spyOn(aiApi, 'troubleshoot').mockResolvedValueOnce(mockAiTroubleshoot);

    render(
      <MemoryRouter initialEntries={['/employee/tickets/50']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
    });

    // Switch to AI tab
    const aiTab = screen.getByRole('button', { name: /AI Troubleshooting Assistant/i });
    fireEvent.click(aiTab);

    // Trigger AI Troubleshooting
    const runBtn = screen.getByRole('button', { name: /Generate AI Advice/i });
    fireEvent.click(runBtn);

    await waitFor(() => {
      expect(aiSpy).toHaveBeenCalledWith(50);
      expect(screen.getByText(/Connections to PostgreSQL pool are exhausting file descriptors./i)).toBeInTheDocument();
      expect(screen.getByText(/Connection leak in API background worker/i)).toBeInTheDocument();
      expect(screen.getByText(/Check pg_stat_activity/i)).toBeInTheDocument();
      expect(screen.getByText(/PostgreSQL Connection Pooling Guide/i)).toBeInTheDocument();
      expect(screen.getByText(/Staging DB connection spikes/i)).toBeInTheDocument();
    });

    // Verify ticket status and priority in header remain untouched
    expect(screen.getAllByText('In Progress').length).toBeGreaterThan(0);
    expect(screen.getByText('Critical')).toBeInTheDocument();
  });

  it('handles AI 503 Service Unavailable gracefully without breaking ticket view', async () => {
    vi.spyOn(ticketsApi, 'getById').mockResolvedValueOnce(mockTicket);
    vi.spyOn(ticketsApi, 'getSla').mockResolvedValueOnce(mockSla);
    vi.spyOn(ticketsApi, 'getHistory').mockResolvedValueOnce([]);
    vi.spyOn(commentsApi, 'getAll').mockResolvedValueOnce([]);

    vi.spyOn(aiApi, 'troubleshoot').mockRejectedValueOnce(
      new ApiClientError(503, 'AI Engine offline')
    );

    render(
      <MemoryRouter initialEntries={['/employee/tickets/50']}>
        <Routes>
          <Route path="/employee/tickets/:id" element={<TicketDetailPage />} />
        </Routes>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
    });

    const aiTab = screen.getByRole('button', { name: /AI Troubleshooting Assistant/i });
    fireEvent.click(aiTab);

    const runBtn = screen.getByRole('button', { name: /Generate AI Advice/i });
    fireEvent.click(runBtn);

    await waitFor(() => {
      expect(
        screen.getByText(/ResolveIQ AI Engine is currently offline or disabled/i)
      ).toBeInTheDocument();
    });

    // Ticket details card remains visible and intact
    expect(screen.getByText('Database connection timeouts')).toBeInTheDocument();
  });
});
