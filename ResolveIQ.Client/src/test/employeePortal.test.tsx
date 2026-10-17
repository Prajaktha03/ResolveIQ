import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { EmployeeDashboard } from '../pages/EmployeeDashboard';
import { CreateTicketPage } from '../pages/CreateTicketPage';
import { MyTicketsPage } from '../pages/MyTicketsPage';
import * as AuthContextModule from '../context/AuthContext';
import { ticketsApi, aiApi, ApiClientError } from '../services/api';
import { Ticket } from '../types';

describe('Employee Portal — Dashboard, Create Ticket & My Tickets', () => {
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
      token: 'jwt-employee-token',
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

  describe('EmployeeDashboard Component', () => {
    it('renders metrics and recent tickets table correctly', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValueOnce({
        totalTickets: 5,
        openTickets: 2,
        inProgressTickets: 1,
        resolvedTickets: 2,
      });

      const mockTickets: Ticket[] = [
        {
          id: 101,
          title: 'Laptop display flicker',
          description: 'Screen flickers when connected to dock',
          status: 'Open',
          priority: 'High',
          createdByUserId: 10,
          createdAt: '2026-10-15T10:00:00Z',
          resolutionDueAt: '2026-10-15T18:00:00Z',
        },
      ];

      vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
        items: mockTickets,
        totalCount: 1,
        page: 1,
        pageSize: 10,
      });

      render(
        <MemoryRouter>
          <EmployeeDashboard />
        </MemoryRouter>
      );

      expect(screen.getByText(/Welcome back, Alice/i)).toBeInTheDocument();

      await waitFor(() => {
        expect(screen.getByText('Laptop display flicker')).toBeInTheDocument();
        expect(screen.getAllByText('Open').length).toBeGreaterThan(0);
        expect(screen.getAllByText('High').length).toBeGreaterThan(0);
      });

      expect(screen.getByText('5')).toBeInTheDocument(); // Total Submitted
    });

    it('renders empty state when employee has no tickets', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockResolvedValueOnce({
        totalTickets: 0,
        openTickets: 0,
        inProgressTickets: 0,
        resolvedTickets: 0,
      });

      vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
      });

      render(
        <MemoryRouter>
          <EmployeeDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/No support tickets found/i)).toBeInTheDocument();
      });
    });

    it('renders error state with retry button upon API failure', async () => {
      vi.spyOn(ticketsApi, 'getDashboard').mockRejectedValueOnce(
        new Error('Failed to reach backend API.')
      );
      vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
      });

      render(
        <MemoryRouter>
          <EmployeeDashboard />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText(/Failed to reach backend API./i)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /Retry/i })).toBeInTheDocument();
      });
    });
  });

  describe('CreateTicketPage Component', () => {
    it('renders ticket form with Title, Description, Priority and SLA information', () => {
      render(
        <MemoryRouter>
          <CreateTicketPage />
        </MemoryRouter>
      );

      expect(screen.getByLabelText(/Subject \/ Summary/i)).toBeInTheDocument();
      expect(screen.getByLabelText(/Detailed Description/i)).toBeInTheDocument();
      expect(screen.getByText(/Service Level Agreement/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Submit Support Ticket/i })).toBeInTheDocument();
    });

    it('submits valid ticket and redirects', async () => {
      const createSpy = vi.spyOn(ticketsApi, 'create').mockResolvedValueOnce({
        id: 999,
        title: 'New Printer Setup',
        description: 'Need drivers installed on workstation',
        status: 'Open',
        priority: 'Medium',
        createdByUserId: 10,
        createdAt: '2026-10-16T12:00:00Z',
      });

      render(
        <MemoryRouter initialEntries={['/employee/tickets/new']}>
          <Routes>
            <Route path="/employee/tickets/new" element={<CreateTicketPage />} />
            <Route path="/employee/tickets/:id" element={<div>Ticket Detail #999</div>} />
          </Routes>
        </MemoryRouter>
      );

      fireEvent.change(screen.getByLabelText(/Subject \/ Summary/i), {
        target: { value: 'New Printer Setup' },
      });
      fireEvent.change(screen.getByLabelText(/Detailed Description/i), {
        target: { value: 'Need drivers installed on workstation' },
      });

      fireEvent.click(screen.getByRole('button', { name: /Submit Support Ticket/i }));

      await waitFor(() => {
        expect(createSpy).toHaveBeenCalledWith({
          title: 'New Printer Setup',
          description: 'Need drivers installed on workstation',
          priority: 'Medium',
        });
        expect(screen.getByText('Ticket Detail #999')).toBeInTheDocument();
      });
    });

    it('preserves user input draft on submission failure', async () => {
      vi.spyOn(ticketsApi, 'create').mockRejectedValueOnce(
        new ApiClientError(400, 'Title contains invalid characters.')
      );

      render(
        <MemoryRouter>
          <CreateTicketPage />
        </MemoryRouter>
      );

      const titleInput = screen.getByLabelText(/Subject \/ Summary/i) as HTMLInputElement;
      const descInput = screen.getByLabelText(/Detailed Description/i) as HTMLTextAreaElement;

      fireEvent.change(titleInput, { target: { value: 'Invalid Title??' } });
      fireEvent.change(descInput, { target: { value: 'Preserved description draft' } });

      fireEvent.click(screen.getByRole('button', { name: /Submit Support Ticket/i }));

      await waitFor(() => {
        expect(screen.getByText(/Title contains invalid characters./i)).toBeInTheDocument();
      });

      // Draft must NOT be cleared on failure
      expect(titleInput.value).toBe('Invalid Title??');
      expect(descInput.value).toBe('Preserved description draft');
    });

    it('requests AI classification and applies priority only when accepted by user', async () => {
      const aiSpy = vi.spyOn(aiApi, 'classifyTicket').mockResolvedValueOnce({
        category: 'Hardware',
        subcategory: 'Monitors',
        suggestedPriority: 'High',
        confidenceScore: 0.92,
        isConfidenceCalibrated: false,
        confidenceCalibrationNote: 'Model-reported heuristic confidence.',
        rationale: 'Multiple displays failing disrupts employee productivity.',
        suggestedTags: ['Hardware', 'Display'],
      });

      render(
        <MemoryRouter>
          <CreateTicketPage />
        </MemoryRouter>
      );

      fireEvent.change(screen.getByLabelText(/Subject \/ Summary/i), {
        target: { value: 'External monitor black screen' },
      });
      fireEvent.change(screen.getByLabelText(/Detailed Description/i), {
        target: { value: 'Dual monitor setup stopped working after restart.' },
      });

      const aiButton = screen.getByRole('button', { name: /Analyze with AI/i });
      fireEvent.click(aiButton);

      await waitFor(() => {
        expect(aiSpy).toHaveBeenCalledWith({
          title: 'External monitor black screen',
          description: 'Dual monitor setup stopped working after restart.',
        });
        expect(screen.getByText(/Hardware › Monitors/i)).toBeInTheDocument();
        expect(screen.getByText(/Multiple displays failing disrupts employee productivity./i)).toBeInTheDocument();
        expect(screen.getByText(/92%/)).toBeInTheDocument();
      });

      // Confirm priority has not been silently changed before clicking apply
      const applyBtn = screen.getByRole('button', { name: /Apply Priority: High/i });
      fireEvent.click(applyBtn);

      await waitFor(() => {
        expect(screen.getByText(/✓ Priority Applied/i)).toBeInTheDocument();
      });
    });

    it('handles AI service unavailable (503) without blocking form submission', async () => {
      vi.spyOn(aiApi, 'classifyTicket').mockRejectedValueOnce(
        new ApiClientError(503, 'AI service unavailable')
      );

      render(
        <MemoryRouter>
          <CreateTicketPage />
        </MemoryRouter>
      );

      fireEvent.change(screen.getByLabelText(/Subject \/ Summary/i), {
        target: { value: 'Test issue' },
      });
      fireEvent.change(screen.getByLabelText(/Detailed Description/i), {
        target: { value: 'Test description content' },
      });

      fireEvent.click(screen.getByRole('button', { name: /Analyze with AI/i }));

      await waitFor(() => {
        expect(
          screen.getByText(/AI Classification Engine is currently offline or unconfigured/i)
        ).toBeInTheDocument();
      });

      // Submit button remains enabled
      expect(screen.getByRole('button', { name: /Submit Support Ticket/i })).not.toBeDisabled();
    });
  });

  describe('MyTicketsPage Component', () => {
    it('renders filtered results and triggers server search', async () => {
      const getAllSpy = vi.spyOn(ticketsApi, 'getAll').mockResolvedValueOnce({
        items: [
          {
            id: 201,
            title: 'VPN connection failure',
            description: 'Error 800 on connection',
            status: 'Open',
            priority: 'Critical',
            createdByUserId: 10,
            createdAt: '2026-10-16T08:00:00Z',
            resolutionDueAt: '2026-10-16T12:00:00Z',
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 10,
      });

      render(
        <MemoryRouter>
          <MyTicketsPage />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(screen.getByText('VPN connection failure')).toBeInTheDocument();
        expect(screen.getAllByText('Critical').length).toBeGreaterThan(0);
      });

      expect(getAllSpy).toHaveBeenCalled();
    });
  });
});
