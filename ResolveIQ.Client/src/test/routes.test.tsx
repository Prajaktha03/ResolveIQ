import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { ProtectedRoute, getRoleHomePath } from '../components/ProtectedRoute';
import * as AuthContextModule from '../context/AuthContext';

describe('Route Protection & Role Redirection', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('getRoleHomePath', () => {
    it('returns /admin/dashboard for Admin role', () => {
      expect(getRoleHomePath('Admin')).toBe('/admin/dashboard');
    });

    it('returns /agent/dashboard for Agent role', () => {
      expect(getRoleHomePath('Agent')).toBe('/agent/dashboard');
    });

    it('returns /employee/dashboard for Employee role', () => {
      expect(getRoleHomePath('Employee')).toBe('/employee/dashboard');
    });

    it('defaults to /employee/dashboard for null or undefined', () => {
      expect(getRoleHomePath(null)).toBe('/employee/dashboard');
      expect(getRoleHomePath(undefined)).toBe('/employee/dashboard');
    });
  });

  describe('ProtectedRoute Component', () => {
    it('redirects unauthenticated users to /login', () => {
      vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
        user: null,
        token: null,
        role: null,
        isAuthenticated: false,
        isLoading: false,
        error: null,
        login: vi.fn(),
        register: vi.fn(),
        logout: vi.fn(),
        clearError: vi.fn(),
      });

      render(
        <MemoryRouter initialEntries={['/admin/dashboard']}>
          <Routes>
            <Route path="/login" element={<div>Login Page</div>} />
            <Route
              path="/admin/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <div>Admin Secret Area</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </MemoryRouter>
      );

      expect(screen.getByText('Login Page')).toBeInTheDocument();
      expect(screen.queryByText('Admin Secret Area')).toBeNull();
    });

    it('allows access when authenticated user has the required role', () => {
      vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
        user: { id: 1, fullName: 'Admin User', email: 'admin@resolveiq.com', role: 'Admin', createdAt: '' },
        token: 'token-abc',
        role: 'Admin',
        isAuthenticated: true,
        isLoading: false,
        error: null,
        login: vi.fn(),
        register: vi.fn(),
        logout: vi.fn(),
        clearError: vi.fn(),
      });

      render(
        <MemoryRouter initialEntries={['/admin/dashboard']}>
          <Routes>
            <Route
              path="/admin/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <div>Admin Secret Area</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </MemoryRouter>
      );

      expect(screen.getByText('Admin Secret Area')).toBeInTheDocument();
    });

    it('redirects to /forbidden when authenticated user does not have required role', () => {
      vi.spyOn(AuthContextModule, 'useAuth').mockReturnValue({
        user: { id: 2, fullName: 'Employee User', email: 'emp@resolveiq.com', role: 'Employee', createdAt: '' },
        token: 'token-abc',
        role: 'Employee',
        isAuthenticated: true,
        isLoading: false,
        error: null,
        login: vi.fn(),
        register: vi.fn(),
        logout: vi.fn(),
        clearError: vi.fn(),
      });

      render(
        <MemoryRouter initialEntries={['/admin/dashboard']}>
          <Routes>
            <Route path="/forbidden" element={<div>Forbidden 403</div>} />
            <Route
              path="/admin/dashboard"
              element={
                <ProtectedRoute allowedRoles={['Admin']}>
                  <div>Admin Secret Area</div>
                </ProtectedRoute>
              }
            />
          </Routes>
        </MemoryRouter>
      );

      expect(screen.getByText('Forbidden 403')).toBeInTheDocument();
      expect(screen.queryByText('Admin Secret Area')).toBeNull();
    });
  });
});
