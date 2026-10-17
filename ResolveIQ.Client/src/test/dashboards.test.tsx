import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { EmployeeDashboard } from '../pages/EmployeeDashboard';
import { AgentDashboard } from '../pages/AgentDashboard';
import { AdminDashboard } from '../pages/AdminDashboard';
import * as AuthContextModule from '../context/AuthContext';
import { ticketsApi, healthApi } from '../services/api';

describe('Role Dashboards Rendering', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('renders EmployeeDashboard with ticket metrics and list', async () => {
    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: { id: 1, fullName: 'John Doe', email: 'john@resolveiq.com', role: 'Employee', createdAt: '' },
      token: 'jwt-123',
      role: 'Employee',
      isAuthenticated: true,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });

    vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValueOnce({
      TotalTickets: 5,
      OpenTickets: 2,
      InProgressTickets: 2,
      ResolvedTickets: 1,
      AtRiskTickets: 0,
      BreachedTickets: 0,
    });

    vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
      items: [
        {
          id: 101,
          title: 'Laptop Keyboard Malfunction',
          description: 'Keys not responding',
          status: 'Open',
          priority: 'Medium',
          createdByUserId: 1,
          createdAt: new Date().toISOString(),
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 10,
    });

    render(
      <MemoryRouter>
        <EmployeeDashboard />
      </MemoryRouter>
    );

    expect(screen.getByText(/Welcome back, John/i)).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Laptop Keyboard Malfunction')).toBeInTheDocument();
      expect(screen.getByText('Total Submitted')).toBeInTheDocument();
    });
  });

  it('renders AgentDashboard with assigned queue and SLA counters', async () => {
    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: { id: 2, fullName: 'Agent Smith', email: 'smith@resolveiq.com', role: 'Agent', createdAt: '' },
      token: 'jwt-456',
      role: 'Agent',
      isAuthenticated: true,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });

    vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValueOnce({
      TotalTickets: 8,
      OpenTickets: 3,
      InProgressTickets: 4,
      ResolvedTickets: 1,
      AtRiskTickets: 2,
      BreachedTickets: 1,
    });

    vi.spyOn(ticketsApi, 'getAssigned').mockResolvedValueOnce({
      items: [
        {
          id: 202,
          title: 'Database connection latency spike',
          description: 'Slow queries',
          status: 'Open',
          priority: 'High',
          createdByUserId: 3,
          assignedToUserId: 2,
          createdAt: new Date().toISOString(),
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 15,
    });

    render(
      <MemoryRouter>
        <AgentDashboard />
      </MemoryRouter>
    );

    expect(screen.getByText(/Agent Desk — Agent Smith/i)).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Database connection latency spike')).toBeInTheDocument();
      expect(screen.getByText('Start Work')).toBeInTheDocument();
    });
  });

  it('renders AdminDashboard with operational metrics and system readiness probe', async () => {
    vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
      user: { id: 3, fullName: 'Admin Chief', email: 'admin@resolveiq.com', role: 'Admin', createdAt: '' },
      token: 'jwt-789',
      role: 'Admin',
      isAuthenticated: true,
      isLoading: false,
      error: null,
      login: vi.fn(),
      register: vi.fn(),
      logout: vi.fn(),
      clearError: vi.fn(),
    });

    vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValueOnce({
      TotalTickets: 42,
      OpenTickets: 12,
      InProgressTickets: 20,
      ResolvedTickets: 10,
      AtRiskTickets: 4,
      BreachedTickets: 2,
    });

    vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 6,
    });

    vi.spyOn(healthApi, 'checkReady').mockResolvedValueOnce({
      status: 'Healthy',
      database: 'Operational',
    });

    render(
      <MemoryRouter>
        <AdminDashboard />
      </MemoryRouter>
    );

    expect(screen.getByText(/IT Service Desk Operations/i)).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('Total Tickets')).toBeInTheDocument();
      expect(screen.getByText('System Readiness')).toBeInTheDocument();
    });
  });
});
