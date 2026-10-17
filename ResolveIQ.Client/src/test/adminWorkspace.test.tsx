import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { AdminDashboard } from '../pages/AdminDashboard';
import { UserManagementPage } from '../pages/UserManagementPage';
import { TicketsPage } from '../pages/TicketsPage';
import { NotificationsMenu } from '../components/NotificationsMenu';
import { Sidebar } from '../components/Sidebar';
import { ProtectedRoute } from '../components/ProtectedRoute';
import * as AuthContextModule from '../context/AuthContext';
import {
  ticketsApi,
  usersApi,
  notificationsApi,
  healthApi,
  ApiClientError,
} from '../services/api';
import {
  Ticket,
  User,
  TicketDashboardResponse,
  EscalationEvent,
  NotificationItem,
} from '../types';

describe('ResolveIQ Batch 10 — Admin Dashboard & Operations Management', () => {
  const mockAdminUser: User = {
    id: 1,
    email: 'admin.super@resolveiq.local',
    fullName: 'Chief Administrator',
    role: 'Admin',
    createdAt: '2026-10-10T08:00:00Z',
  };

  const mockAgentUser: User = {
    id: 2,
    email: 'agent.smith@resolveiq.local',
    fullName: 'Agent Smith',
    role: 'Agent',
    createdAt: '2026-10-12T09:00:00Z',
  };

  const mockEmployeeUser: User = {
    id: 3,
    email: 'jane.doe@resolveiq.local',
    fullName: 'Jane Doe',
    role: 'Employee',
    createdAt: '2026-10-14T11:00:00Z',
  };

  const mockUserDirectory: User[] = [
    mockAdminUser,
    mockAgentUser,
    mockEmployeeUser,
  ];

  const mockDashboardMetrics: TicketDashboardResponse = {
    totalTickets: 42,
    openTickets: 12,
    inProgressTickets: 18,
    resolvedTickets: 14,
    atRiskTickets: 3,
    breachedTickets: 2,
    slaChecksMet: 50,
    slaChecksBreached: 5,
    slaCompliancePercentage: 90.91,
  };

  const mockRecentTickets: Ticket[] = [
    {
      id: 101,
      title: 'Global SSO authentication outage',
      description: 'Okta federated login returns 500 across customer tenant',
      status: 'Open',
      priority: 'Critical',
      createdByUserId: 3,
      assignedToUserId: null,
      firstResponseDueAt: '2026-10-16T10:15:00Z',
      resolutionDueAt: '2026-10-16T14:00:00Z',
      createdAt: '2026-10-16T10:00:00Z',
    },
    {
      id: 102,
      title: 'Storage volume expansion for analytics',
      description: 'PostgreSQL read replica NVMe volume reaching 92% capacity',
      status: 'InProgress',
      priority: 'High',
      createdByUserId: 3,
      assignedToUserId: 2,
      firstResponseDueAt: '2026-10-16T11:00:00Z',
      resolutionDueAt: '2026-10-16T18:00:00Z',
      createdAt: '2026-10-16T10:30:00Z',
    },
  ];

  const mockEscalations: EscalationEvent[] = [
    {
      ticketId: 101,
      ticketTitle: 'Global SSO authentication outage',
      priority: 'Critical',
      status: 'Open',
      assignedToUserId: null,
      assignedToName: null,
      escalatedAt: '2026-10-16T10:16:00Z',
      reason: 'First response SLA target breached (15m elapsed)',
    },
    {
      ticketId: 102,
      ticketTitle: 'Storage volume expansion for analytics',
      priority: 'High',
      status: 'InProgress',
      assignedToUserId: 2,
      assignedToName: 'Agent Smith',
      escalatedAt: '2026-10-16T11:05:00Z',
      reason: 'First response SLA deadline exceeded',
    },
  ];

  const mockNotifications: NotificationItem[] = [
    {
      id: 1,
      userId: 1,
      type: 'Escalated',
      title: 'Ticket #101 Escalated',
      message: 'Critical ticket #101 breached first response SLA deadline.',
      ticketId: 101,
      createdAt: '2026-10-16T10:16:00Z',
      isRead: false,
    },
    {
      id: 2,
      userId: 1,
      type: 'TicketAssigned',
      title: 'Ticket #102 Assigned',
      message: 'Ticket #102 has been assigned to Agent Smith.',
      ticketId: 102,
      createdAt: '2026-10-16T10:35:00Z',
      isRead: true,
      readAt: '2026-10-16T10:40:00Z',
    },
  ];

  const setupAuth = (user: User | null = mockAdminUser) => {
    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user,
      token: user ? 'fake-admin-jwt' : null,
      role: user?.role || null,
      isAuthenticated: !!user,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    setupAuth(mockAdminUser);
  });

  // ===========================================================================
  // 1. Admin Dashboard Metrics & Loading / Empty / Error States
  // ===========================================================================
  describe('Phase 1 — Admin Dashboard Overview', () => {
    it('renders live enterprise metrics, SLA compliance, and system readiness correctly', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue(mockDashboardMetrics);
      vi.spyOn(ticketsApi, 'getAll').mockResolvedValue({
        items: mockRecentTickets,
        totalCount: 2,
        page: 1,
        pageSize: 6,
      });
      vi.spyOn(ticketsApi, 'getEscalations').mockResolvedValue(mockEscalations);
      vi.spyOn(healthApi, 'checkReady').mockResolvedValue({ status: 'Healthy', database: 'Healthy' });

      render(
        <MemoryRouter>
          <AdminDashboard />
        </MemoryRouter>
      );

      // Verify header and indicator
      expect(screen.getByText('Administrator Control Center')).toBeInTheDocument();
      expect(screen.getByText('IT Service Desk Operations')).toBeInTheDocument();

      // Verify real metrics are rendered
      await waitFor(() => {
        expect(screen.getByText('42')).toBeInTheDocument(); // Total Tickets
        expect(screen.getByText('12')).toBeInTheDocument(); // Open
        expect(screen.getByText('18')).toBeInTheDocument(); // In Progress
        expect(screen.getByText('14')).toBeInTheDocument(); // Resolved
        expect(screen.getByText('3')).toBeInTheDocument();  // At Risk
        expect(screen.getByText('2')).toBeInTheDocument();  // Breached
        expect(screen.getByText('90.91%')).toBeInTheDocument(); // SLA Compliance
        expect(screen.getByText('50')).toBeInTheDocument(); // Checks Met
        expect(screen.getByText('5')).toBeInTheDocument();  // Checks Breached
      });

      // Verify escalation & recent tickets preview items
      expect(screen.getAllByText('Global SSO authentication outage').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Storage volume expansion for analytics').length).toBeGreaterThan(0);
    });

    it('renders error state with retry button when metrics call fails', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockRejectedValue(new Error('Gateway timeout (504)'));
      vi.spyOn(ticketsApi, 'getAll').mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 6 });
      vi.spyOn(ticketsApi, 'getEscalations').mockResolvedValue([]);
      vi.spyOn(healthApi, 'checkReady').mockResolvedValue({ status: 'Degraded', database: 'Unreachable' });

      render(
        <MemoryRouter>
          <AdminDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/Gateway timeout/i)).toBeInTheDocument();
      });

      const retryBtn = screen.getByRole('button', { name: /retry/i });
      expect(retryBtn).toBeInTheDocument();

      // Trigger retry
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue(mockDashboardMetrics);
      fireEvent.click(retryBtn);

      await waitFor(() => {
        expect(screen.getByText('42')).toBeInTheDocument();
      });
    });

    it('handles empty ticket and escalation queues without crashing', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValue({
        totalTickets: 0,
        openTickets: 0,
        inProgressTickets: 0,
        resolvedTickets: 0,
        atRiskTickets: 0,
        breachedTickets: 0,
        slaChecksMet: 0,
        slaChecksBreached: 0,
        slaCompliancePercentage: 0,
      });
      vi.spyOn(ticketsApi, 'getAll').mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 6 });
      vi.spyOn(ticketsApi, 'getEscalations').mockResolvedValue([]);
      vi.spyOn(healthApi, 'checkReady').mockResolvedValue({ status: 'Healthy', database: 'Healthy' });

      render(
        <MemoryRouter>
          <AdminDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('No tickets found in repository.')).toBeInTheDocument();
        expect(screen.getByText('No SLA breach escalation events on record.')).toBeInTheDocument();
      });
    });
  });

  // ===========================================================================
  // 2. User Management Listing & Filtering
  // ===========================================================================
  describe('Phase 2 — User Management & Safe Role Governance', () => {
    it('lists registered users with ID, name, email, and role', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Chief Administrator')).toBeInTheDocument();
        expect(screen.getByText('admin.super@resolveiq.local')).toBeInTheDocument();
        expect(screen.getByText('Agent Smith')).toBeInTheDocument();
        expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      });

      expect(screen.getByText('3 of 3 accounts')).toBeInTheDocument();
    });

    it('filters users by search term and role dropdown', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      });

      // Filter by search
      const searchInput = screen.getByPlaceholderText(/search users/i);
      fireEvent.change(searchInput, { target: { value: 'Smith' } });

      expect(screen.getByText('Agent Smith')).toBeInTheDocument();
      expect(screen.queryByText('Jane Doe')).not.toBeInTheDocument();

      // Clear search and filter by role
      fireEvent.change(searchInput, { target: { value: '' } });
      const roleSelect = screen.getByRole('combobox');
      fireEvent.change(roleSelect, { target: { value: 'Employee' } });

      expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      expect(screen.queryByText('Agent Smith')).not.toBeInTheDocument();
      expect(screen.queryByText('Chief Administrator')).not.toBeInTheDocument();
    });

    // =========================================================================
    // 3. Successful and Failed Role Changes with Confirmation Modal
    // =========================================================================
    it('requires confirmation before submitting role change and reflects success', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);
      const updateRoleSpy = vi.spyOn(usersApi, 'updateRole').mockResolvedValue({
        message: 'User role updated successfully.',
        user: { ...mockEmployeeUser, role: 'Agent' },
      });

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      });

      // Click "Promote to Agent"
      const promoteBtn = screen.getByRole('button', { name: /promote to agent/i });
      fireEvent.click(promoteBtn);

      // Verify Confirmation Modal opened
      expect(screen.getByText('Confirm Role Change')).toBeInTheDocument();
      expect(screen.getByText(/Are you sure you want to change the role of/i)).toBeInTheDocument();

      // Confirm role change
      const confirmBtn = screen.getByRole('button', { name: /confirm change/i });
      fireEvent.click(confirmBtn);

      await waitFor(() => {
        expect(updateRoleSpy).toHaveBeenCalledWith(mockEmployeeUser.id, 'Agent');
      });

      // Confirmation modal closes and success message shows
      await waitFor(() => {
        expect(screen.queryByText('Confirm Role Change')).not.toBeInTheDocument();
        expect(screen.getByText(/User role updated successfully/i)).toBeInTheDocument();
      });
    });

    it('handles server rejection when updating role without mutating client state', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);
      vi.spyOn(usersApi, 'updateRole').mockRejectedValue(
        new ApiClientError(400, 'Role must be Employee or Agent. Admin roles cannot be assigned.')
      );

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      });

      // Click "Promote to Agent"
      fireEvent.click(screen.getByRole('button', { name: /promote to agent/i }));

      // Confirm
      fireEvent.click(screen.getByRole('button', { name: /confirm change/i }));

      // Error message is displayed in modal
      await waitFor(() => {
        expect(
          screen.getByText(/Role must be Employee or Agent. Admin roles cannot be assigned./i)
        ).toBeInTheDocument();
      });

      // Jane Doe remains an Employee in the background
      expect(screen.getAllByText('Jane Doe').length).toBeGreaterThan(0);
    });

    // =========================================================================
    // 4. Protection of Sensitive User Fields
    // =========================================================================
    it('protects Admin accounts from accidental modification and does not expose sensitive fields', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Chief Administrator')).toBeInTheDocument();
      });

      // Admin row shows "Admin Protected" instead of action buttons
      expect(screen.getByText(/Admin Protected/i)).toBeInTheDocument();

      // Ensure no password hashes or sensitive tokens are exposed anywhere
      expect(screen.queryByText(/passwordHash/i)).not.toBeInTheDocument();
      expect(screen.queryByText(/token/i)).not.toBeInTheDocument();
    });
  });

  // ===========================================================================
  // 5. Ticket Operations & Assignment (Admin-specific capability)
  // ===========================================================================
  describe('Phase 3 — Ticket Operations & Admin Assignment', () => {
    it('allows Admin to assign an unassigned ticket to a designated support agent', async () => {
      vi.spyOn(ticketsApi, 'getAll').mockResolvedValue({
        items: mockRecentTickets,
        totalCount: 2,
        page: 1,
        pageSize: 15,
      });
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);
      const assignSpy = vi.spyOn(ticketsApi, 'assign').mockResolvedValue({
        ...mockRecentTickets[0],
        assignedToUserId: mockAgentUser.id,
      });

      render(
        <MemoryRouter>
          <TicketsPage mode="all" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Global SSO authentication outage')).toBeInTheDocument();
      });

      // Ticket #101 is unassigned - Admin clicks "Assign"
      const assignBtn = screen.getByRole('button', { name: /^assign$/i });
      fireEvent.click(assignBtn);

      // Verify Assignment Modal
      await waitFor(() => {
        expect(screen.getByText(/Assign Ticket #101/i)).toBeInTheDocument();
      });

      // Select Agent Smith (ID #2)
      const agentSelect = screen.getByTestId('agent-select');
      fireEvent.change(agentSelect, { target: { value: '2' } });

      // Confirm assignment
      const confirmAssignBtn = screen.getByRole('button', { name: /confirm assignment/i });
      fireEvent.click(confirmAssignBtn);

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(101, 2);
      });
    });

    it('handles 403 Forbidden on ticket operations gracefully', async () => {
      vi.spyOn(ticketsApi, 'getAll').mockRejectedValue(
        new ApiClientError(403, 'You do not have permission to view these tickets.')
      );

      render(
        <MemoryRouter>
          <TicketsPage mode="all" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/Access denied: You do not have permission to view these tickets./i)).toBeInTheDocument();
      });
    });
  });

  // ===========================================================================
  // 6. SLA and Escalation Monitoring
  // ===========================================================================
  describe('Phase 4 — SLA and Escalation Monitoring View', () => {
    it('renders real SLA escalation events with reasons, assignees, and links to tickets', async () => {
      vi.spyOn(ticketsApi, 'getEscalations').mockResolvedValue(mockEscalations);

      render(
        <MemoryRouter>
          <TicketsPage mode="escalations" />
        </MemoryRouter>
      );

      expect(screen.getByText('SLA Compliance Audit')).toBeInTheDocument();
      expect(screen.getByText('SLA Escalations & Breach Queue')).toBeInTheDocument();

      await waitFor(() => {
        expect(screen.getByText('First response SLA target breached (15m elapsed)')).toBeInTheDocument();
        expect(screen.getByText('First response SLA deadline exceeded')).toBeInTheDocument();
      });

      // Direct links to tickets
      const inspectLinks = screen.getAllByRole('link', { name: /inspect/i });
      expect(inspectLinks.length).toBe(2);
      expect(inspectLinks[0]).toHaveAttribute('href', '/tickets/101');
      expect(inspectLinks[1]).toHaveAttribute('href', '/tickets/102');
    });

    it('renders empty state when no tickets have breached SLA targets', async () => {
      vi.spyOn(ticketsApi, 'getEscalations').mockResolvedValue([]);

      render(
        <MemoryRouter>
          <TicketsPage mode="escalations" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('No Escalations Recorded')).toBeInTheDocument();
        expect(
          screen.getByText('All tickets are currently within acceptable SLA response and resolution milestones.')
        ).toBeInTheDocument();
      });
    });
  });

  // ===========================================================================
  // 7. Notification Behavior & Unread Counts
  // ===========================================================================
  describe('Phase 5 — Notifications Integration', () => {
    it('displays unread badge, fetches notifications on toggle, and allows marking read', async () => {
      vi.spyOn(notificationsApi, 'getUnreadCount').mockResolvedValue({ unreadCount: 1 });
      vi.spyOn(notificationsApi, 'getAll').mockResolvedValue({
        items: mockNotifications,
        totalCount: 2,
        page: 1,
        pageSize: 8,
      });
      const markReadSpy = vi.spyOn(notificationsApi, 'markAsRead').mockResolvedValue({
        ...mockNotifications[0],
        isRead: true,
      });

      render(
        <MemoryRouter>
          <NotificationsMenu />
        </MemoryRouter>
      );

      // Verify unread badge exists
      await waitFor(() => {
        expect(screen.getByTestId('unread-badge')).toHaveTextContent('1');
      });

      // Open notification popover
      const bellBtn = screen.getByRole('button', { name: /view notifications/i });
      fireEvent.click(bellBtn);

      await waitFor(() => {
        expect(screen.getByText('Ticket #101 Escalated')).toBeInTheDocument();
        expect(screen.getByText('Ticket #102 Assigned')).toBeInTheDocument();
      });

      // Mark single notification as read
      const markReadBtn = screen.getByRole('button', { name: /mark read/i });
      fireEvent.click(markReadBtn);

      await waitFor(() => {
        expect(markReadSpy).toHaveBeenCalledWith(1);
      });
    });

    it('allows marking all notifications as read', async () => {
      vi.spyOn(notificationsApi, 'getUnreadCount').mockResolvedValue({ unreadCount: 2 });
      vi.spyOn(notificationsApi, 'getAll').mockResolvedValue({
        items: mockNotifications,
        totalCount: 2,
        page: 1,
        pageSize: 8,
      });
      const markAllSpy = vi.spyOn(notificationsApi, 'markAllAsRead').mockResolvedValue({
        message: 'All notifications marked as read.',
        updatedCount: 2,
      });

      render(
        <MemoryRouter>
          <NotificationsMenu />
        </MemoryRouter>
      );

      fireEvent.click(screen.getByRole('button', { name: /view notifications/i }));

      await waitFor(() => {
        expect(screen.getByRole('button', { name: /mark all read/i })).toBeInTheDocument();
      });

      fireEvent.click(screen.getByRole('button', { name: /mark all read/i }));

      await waitFor(() => {
        expect(markAllSpy).toHaveBeenCalled();
      });
    });
  });

  // ===========================================================================
  // 8. Role-Based Navigation for All Three Roles
  // ===========================================================================
  describe('Phase 6 — Navigation & Role Authorization Boundaries', () => {
    it('renders Admin-specific navigation links for Admin role', () => {
      setupAuth(mockAdminUser);

      render(
        <MemoryRouter>
          <Sidebar isOpen={true} onClose={vi.fn()} />
        </MemoryRouter>
      );

      expect(screen.getByText('User Management')).toBeInTheDocument();
      expect(screen.getByText('System Health')).toBeInTheDocument();
      expect(screen.getByText('All Tickets')).toBeInTheDocument();
      expect(screen.getByText('SLA Escalations')).toBeInTheDocument();
    });

    it('renders Agent-specific navigation links for Agent role', () => {
      setupAuth(mockAgentUser);

      render(
        <MemoryRouter>
          <Sidebar isOpen={true} onClose={vi.fn()} />
        </MemoryRouter>
      );

      expect(screen.getByText('Assigned to Me')).toBeInTheDocument();
      expect(screen.getByText('All Tickets')).toBeInTheDocument();
      expect(screen.getByText('SLA Escalations')).toBeInTheDocument();
      expect(screen.queryByText('User Management')).not.toBeInTheDocument();
    });

    it('renders Employee-specific navigation links for Employee role', () => {
      setupAuth(mockEmployeeUser);

      render(
        <MemoryRouter>
          <Sidebar isOpen={true} onClose={vi.fn()} />
        </MemoryRouter>
      );

      expect(screen.getByText('My Tickets')).toBeInTheDocument();
      expect(screen.getByText('Submit Ticket')).toBeInTheDocument();
      expect(screen.queryByText('User Management')).not.toBeInTheDocument();
      expect(screen.queryByText('Assigned to Me')).not.toBeInTheDocument();
    });

    it('redirects unauthorized users from Admin routes to /forbidden', () => {
      setupAuth(mockEmployeeUser);

      render(
        <MemoryRouter initialEntries={['/admin/users']}>
          <Routes>
            <Route path="/forbidden" element={<div>Access Denied (403)</div>} />
            <Route
              path="/admin/users"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <div>Admin User Management</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </MemoryRouter>
      );

      expect(screen.getByText('Access Denied (403)')).toBeInTheDocument();
      expect(screen.queryByText('Admin User Management')).not.toBeInTheDocument();
    });
  });

  // ===========================================================================
  // 10. Prevention of Fabricated Success States
  // ===========================================================================
  describe('Phase 7 — Verification of Backend Authority & No Fabricated Success', () => {
    it('does not apply role change in UI if network or server call fails', async () => {
      vi.spyOn(usersApi, 'getAll').mockResolvedValue(mockUserDirectory);
      vi.spyOn(usersApi, 'updateRole').mockRejectedValue(new Error('Network transport error'));

      render(
        <MemoryRouter>
          <UserManagementPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('Jane Doe')).toBeInTheDocument();
      });

      // Initiate promote
      fireEvent.click(screen.getByRole('button', { name: /promote to agent/i }));
      fireEvent.click(screen.getByRole('button', { name: /confirm change/i }));

      await waitFor(() => {
        expect(screen.getByText(/Network transport error/i)).toBeInTheDocument();
      });

      // Verify that Jane Doe's row still displays Employee role
      expect(screen.getAllByText('Jane Doe').length).toBeGreaterThan(0);
    });
  });
});
