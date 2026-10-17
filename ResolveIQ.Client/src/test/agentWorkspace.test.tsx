import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { AgentDashboard } from '../pages/AgentDashboard';
import { AssignedTicketsPage } from '../pages/AssignedTicketsPage';
import { TicketDetailPage } from '../pages/TicketDetailPage';
import * as AuthContextModule from '../context/AuthContext';
import { ticketsApi, commentsApi, aiApi, ApiClientError } from '../services/api';
import {
  Ticket,
  TicketComment,
  TicketHistory,
  TicketSlaStatus,
  TicketDashboardResponse,
  PagedResult,
  PriorityRecommendationResult,
  User,
} from '../types';

describe('Agent Workspace — Dashboard, Assigned Queue, Ticket Management & AI Support', () => {
  const mockAgentUser: User = {
    id: 5,
    email: 'agent.smith@resolveiq.local',
    fullName: 'Agent Smith',
    role: 'Agent' as const,
    createdAt: '2026-10-15T08:00:00Z',
  };

  const mockTickets: Ticket[] = [
    {
      id: 101,
      title: 'VPN gateway latency degradation',
      description: 'Multiple users in Region East experiencing intermittent disconnects',
      status: 'Open',
      priority: 'High',
      createdByUserId: 20,
      assignedToUserId: 5,
      createdAt: '2026-10-16T08:00:00Z',
      firstResponseDueAt: '2026-10-16T09:00:00Z',
      resolutionDueAt: '2026-10-16T16:00:00Z',
    },
    {
      id: 102,
      title: 'Database connection pool failure',
      description: 'PostgreSQL max connections threshold reached',
      status: 'InProgress',
      priority: 'Critical',
      createdByUserId: 21,
      assignedToUserId: 5,
      createdAt: '2026-10-16T08:30:00Z',
      firstResponseDueAt: '2026-10-16T08:45:00Z',
      firstRespondedAt: '2026-10-16T08:40:00Z',
      resolutionDueAt: '2026-10-16T12:30:00Z',
    },
    {
      id: 103,
      title: 'Monitor peripheral request',
      description: 'Request for secondary 4K display for developer workstation',
      status: 'Resolved',
      priority: 'Low',
      createdByUserId: 22,
      assignedToUserId: 5,
      createdAt: '2026-10-15T10:00:00Z',
      resolvedAt: '2026-10-15T15:00:00Z',
    },
  ];

  const mockDashboardMetrics: TicketDashboardResponse = {
    totalTickets: 3,
    openTickets: 1,
    inProgressTickets: 1,
    resolvedTickets: 1,
    atRiskTickets: 1,
    breachedTickets: 0,
  };

  const mockPagedAssignedResult: PagedResult<Ticket> = {
    items: mockTickets,
    totalCount: 3,
    page: 1,
    pageSize: 10,
  };

  const mockSla: TicketSlaStatus = {
    ticketId: 101,
    firstResponseDueAt: '2026-10-16T09:00:00Z',
    firstRespondedAt: null,
    firstResponseStatus: 'On Track',
    resolutionDueAt: '2026-10-16T16:00:00Z',
    resolvedAt: null,
    resolutionStatus: 'On Track',
  };

  const mockHistory: TicketHistory[] = [
    {
      id: 1,
      ticketId: 101,
      userId: 20,
      actorName: 'Alice Employee',
      actorRole: 'Employee',
      action: 'Created',
      oldValue: null,
      newValue: 'Ticket created with priority High',
      createdAt: '2026-10-16T08:00:00Z',
    },
    {
      id: 2,
      ticketId: 101,
      userId: 1,
      actorName: 'Admin System',
      actorRole: 'Admin',
      action: 'Assigned',
      oldValue: null,
      newValue: 'Assigned to Agent Smith',
      createdAt: '2026-10-16T08:05:00Z',
    },
  ];

  const mockComments: TicketComment[] = [
    {
      id: 1,
      ticketId: 101,
      userId: 20,
      author: 'Alice Employee',
      content: 'VPN client reports connection timeout after 30 seconds.',
      createdAt: '2026-10-16T08:02:00Z',
    },
  ];

  beforeEach(() => {
    vi.restoreAllMocks();

    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: mockAgentUser,
      token: 'mock-agent-jwt',
      role: 'Agent',
      isAuthenticated: true,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });
  });

  describe('AgentDashboard Component', () => {
    it('renders scoped metrics and urgent priority tickets correctly', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue(mockDashboardMetrics);
      vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue(mockPagedAssignedResult);

      render(
        <MemoryRouter>
          <AgentDashboard />
        </MemoryRouter>
      );

      // Loading state initially
      expect(screen.getByText(/Fetching assigned queue/i)).toBeInTheDocument();

      // Welcome header
      await waitFor(() => {
        expect(screen.getByText(/Agent Desk — Agent Smith/i)).toBeInTheDocument();
      });

      // Metrics cards
      expect(screen.getByText('Assigned Total')).toBeInTheDocument();
      expect(screen.getAllByText('Open').length).toBeGreaterThanOrEqual(1);
      expect(screen.getAllByText('In Progress').length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText('High/Critical')).toBeInTheDocument();

      // Queue items
      expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      expect(screen.getByText('Database connection pool failure')).toBeInTheDocument();
    });

    it('displays SLA warning banner when tickets are at risk or breached', async () => {
      const atRiskMetrics: TicketDashboardResponse = {
        totalTickets: 4,
        openTickets: 2,
        inProgressTickets: 1,
        resolvedTickets: 1,
        atRiskTickets: 1,
        breachedTickets: 1,
      };

      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue(atRiskMetrics);
      vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue(mockPagedAssignedResult);

      render(
        <MemoryRouter>
          <AgentDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/SLA Alert:/i)).toBeInTheDocument();
        expect(screen.getByText(/1 ticket\(s\) at risk/i)).toBeInTheDocument();
        expect(screen.getByText(/1 breached ticket\(s\)/i)).toBeInTheDocument();
      });
    });

    it('handles dashboard loading error and allows retry', async () => {
      const getDashboardSpy = vi.spyOn(ticketsApi, 'getDashboard')
        .mockRejectedValueOnce(new Error('Network timeout connecting to dashboard API'))
        .mockResolvedValueOnce(mockDashboardMetrics);

      vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue(mockPagedAssignedResult);

      render(
        <MemoryRouter>
          <AgentDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Failed to Load Dashboard')).toBeInTheDocument();
        expect(screen.getByText('Network timeout connecting to dashboard API')).toBeInTheDocument();
      });

      // Click retry
      fireEvent.click(screen.getByRole('button', { name: /Retry/i }));

      await waitFor(() => {
        expect(screen.getByText(/Agent Desk — Agent Smith/i)).toBeInTheDocument();
      });
      expect(getDashboardSpy).toHaveBeenCalledTimes(2);
    });

    it('performs quick status transition from dashboard while preserving ticket priority', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue(mockDashboardMetrics);
      vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue(mockPagedAssignedResult);

      const updateSpy = vi.spyOn(ticketsApi, 'update').mockResolvedValue({
        ...mockTickets[0],
        status: 'InProgress',
      });

      render(
        <MemoryRouter>
          <AgentDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Find quick action button "Start Work" for Ticket #101
      const startButton = screen.getByRole('button', { name: /Start Work/i });
      fireEvent.click(startButton);

      await waitFor(() => {
        expect(updateSpy).toHaveBeenCalledWith(101, {
          status: 'InProgress',
          priority: 'High', // Priority explicitly preserved
        });
      });
    });
  });

  describe('AssignedTicketsPage Component', () => {
    it('renders assigned ticket list with search, status filters, and pagination', async () => {
      const getAssignedSpy = vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue(mockPagedAssignedResult);

      render(
        <MemoryRouter>
          <AssignedTicketsPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Assigned Tickets Workspace')).toBeInTheDocument();
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Test searching
      const searchInput = screen.getByPlaceholderText(/Search by ticket title/i);
      fireEvent.change(searchInput, { target: { value: 'gateway' } });

      await waitFor(() => {
        expect(getAssignedSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            search: 'gateway',
          })
        );
      });

      // Test status filter
      const statusSelect = screen.getByLabelText('Filter by Status');
      fireEvent.change(statusSelect, { target: { value: 'Open' } });

      await waitFor(() => {
        expect(getAssignedSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            status: 'Open',
          })
        );
      });
    });

    it('handles empty assigned queue gracefully', async () => {
      vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValue({
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
      });

      render(
        <MemoryRouter>
          <AssignedTicketsPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/No assigned tickets found/i)).toBeInTheDocument();
        expect(screen.getByText(/You currently have no tickets assigned/i)).toBeInTheDocument();
      });
    });
  });

  describe('TicketDetailPage — Agent Experience & SLA Workflow', () => {
    it('renders agent operations panel and role-aware navigation', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Role-aware back button
      expect(screen.getByText('Back to Assigned Tickets').closest('a')).toHaveAttribute('href', '/agent/assigned');

      // Agent Operations Panel
      expect(screen.getByText('Agent Operations & Ticket Controls')).toBeInTheDocument();
      expect(screen.getByText('Assigned to You')).toBeInTheDocument();

      // SLA Milestones
      expect(screen.getByText('First Response Milestone:')).toBeInTheDocument();
      expect(screen.getByText('Resolution Milestone:')).toBeInTheDocument();
    });

    it('successfully updates ticket status and priority and refreshes SLA and history', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      const getSlaSpy = vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      const getHistorySpy = vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      const updateSpy = vi.spyOn(ticketsApi, 'update').mockResolvedValue({
        ...mockTickets[0],
        status: 'InProgress',
        priority: 'High',
      });

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Click "In Progress" button in Agent Operations
      const inProgressBtn = screen.getByRole('button', { name: /In Progress/i });
      fireEvent.click(inProgressBtn);

      await waitFor(() => {
        expect(updateSpy).toHaveBeenCalledWith(101, {
          status: 'InProgress',
          priority: 'High',
        });
      });

      // Confirms SLA and history refresh was triggered
      expect(getSlaSpy).toHaveBeenCalledTimes(2);
      expect(getHistorySpy).toHaveBeenCalledTimes(2);

      // Flash success message
      expect(screen.getByText(/Ticket #101 successfully updated to InProgress/i)).toBeInTheDocument();
    });

    it('handles 403 Forbidden on update when ticket is not assigned to agent', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      vi.spyOn(ticketsApi, 'update').mockRejectedValue(
        new ApiClientError(403, 'Forbidden: Only the assigned agent can modify this ticket.')
      );

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Try setting status to Resolved
      const resolvedBtn = screen.getByRole('button', { name: /Resolved/i });
      fireEvent.click(resolvedBtn);

      await waitFor(() => {
        expect(screen.getByText(/Access Restricted: You are only authorized to update tickets assigned to your queue/i)).toBeInTheDocument();
      });
    });

    it('submits support response and preserves comment draft on network error', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      const createCommentSpy = vi.spyOn(commentsApi, 'create')
        .mockRejectedValueOnce(new ApiClientError(500, 'Connection error posting comment'))
        .mockResolvedValueOnce({
          id: 2,
          ticketId: 101,
          userId: 5,
          author: 'Agent Smith',
          content: 'Confirmed gateway failover completed. Traffic re-routed to secondary link.',
          createdAt: '2026-10-16T08:45:00Z',
        });

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      const textarea = screen.getByPlaceholderText(/Write your official support response/i);
      fireEvent.change(textarea, { target: { value: 'Confirmed gateway failover completed.' } });

      const submitBtn = screen.getByRole('button', { name: /Submit Response/i });
      fireEvent.click(submitBtn);

      // On failure: Error banner shown, draft retained
      await waitFor(() => {
        expect(screen.getByText('Connection error posting comment')).toBeInTheDocument();
      });
      expect(textarea).toHaveValue('Confirmed gateway failover completed.');

      // Second attempt succeeds
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(createCommentSpy).toHaveBeenCalledTimes(2);
      });
    });
  });

  describe('AI Intelligence & Advisory Priority Support', () => {
    it('evaluates priority recommendations without mutating ticket fields automatically', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      const mockAiRecommendation: PriorityRecommendationResult = {
        currentPriority: 'High',
        recommendedPriority: 'Critical',
        confidenceScore: 0.94,
        isConfidenceCalibrated: true,
        confidenceCalibrationNote: 'High confidence based on enterprise outage history',
        rationale: 'VPN gateway outage impacts over 500 remote employees simultaneously, warranting Critical priority.',
        uncertaintyOrMissingInfo: null,
        slaFirstResponseTarget: '15 minutes',
        slaResolutionTarget: '4 hours',
      };

      const recommendSpy = vi.spyOn(aiApi, 'recommendPriority').mockResolvedValue(mockAiRecommendation);
      const updateSpy = vi.spyOn(ticketsApi, 'update').mockResolvedValue({
        ...mockTickets[0],
        priority: 'Critical',
      });

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Switch to AI tab
      const aiTab = screen.getByRole('button', { name: /AI Troubleshooting Assistant/i });
      fireEvent.click(aiTab);

      // Run Priority Evaluation
      const evalBtn = screen.getByRole('button', { name: /Evaluate Priority/i });
      fireEvent.click(evalBtn);

      await waitFor(() => {
        expect(recommendSpy).toHaveBeenCalledWith({ ticketId: 101 });
      });

      // Rationale and confidence displayed
      expect(screen.getByText(/VPN gateway outage impacts over 500 remote employees/i)).toBeInTheDocument();
      expect(screen.getByText('94%')).toBeInTheDocument();

      // Zero silent mutation: update API has NOT been called yet
      expect(updateSpy).not.toHaveBeenCalled();

      // Explicit adoption button
      const applyBtn = screen.getByRole('button', { name: /Apply Critical Priority/i });
      fireEvent.click(applyBtn);

      await waitFor(() => {
        expect(updateSpy).toHaveBeenCalledWith(101, {
          status: 'Open',
          priority: 'Critical',
        });
      });
    });

    it('handles AI service offline (503) and rate-limited (429) states gracefully', async () => {
      vi.spyOn(ticketsApi, 'getById').mockResolvedValue(mockTickets[0]);
      vi.spyOn(ticketsApi, 'getSla').mockResolvedValue(mockSla);
      vi.spyOn(ticketsApi, 'getHistory').mockResolvedValue(mockHistory);
      vi.spyOn(commentsApi, 'getAll').mockResolvedValue(mockComments);

      vi.spyOn(aiApi, 'troubleshoot').mockRejectedValue(
        new ApiClientError(503, 'AI service unavailable')
      );

      render(
        <MemoryRouter initialEntries={['/agent/tickets/101']}>
          <Routes>
            <Route path="/agent/tickets/:id" element={<TicketDetailPage />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
      });

      // Switch to AI tab
      const aiTab = screen.getByRole('button', { name: /AI Troubleshooting Assistant/i });
      fireEvent.click(aiTab);

      // Request troubleshooting advice
      const adviceBtn = screen.getByRole('button', { name: /Generate AI Advice/i });
      fireEvent.click(adviceBtn);

      await waitFor(() => {
        expect(screen.getByText(/ResolveIQ AI Engine is currently offline or disabled on the server/i)).toBeInTheDocument();
      });

      // Verify the rest of the ticket view is completely functional
      expect(screen.getByText('VPN gateway latency degradation')).toBeInTheDocument();
    });
  });
});
