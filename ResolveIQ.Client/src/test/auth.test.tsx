import React from 'react';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { AuthProvider, useAuth } from '../context/AuthContext';
import { authApi, getStoredToken } from '../services/api';

const TestAuthConsumer: React.FC = () => {
  const { user, token, isAuthenticated, login, logout, register, error } = useAuth();

  return (
    <div>
      <div data-testid="auth-status">{isAuthenticated ? 'authenticated' : 'unauthenticated'}</div>
      <div data-testid="user-role">{user?.role || 'none'}</div>
      <div data-testid="user-email">{user?.email || 'none'}</div>
      <div data-testid="token-val">{token || 'none'}</div>
      <div data-testid="error-msg">{error || 'none'}</div>
      <button
        onClick={() => login('admin@resolveiq.com', 'Password123!')}
        data-testid="login-btn"
      >
        Login
      </button>
      <button
        onClick={() => register('Test User', 'new@resolveiq.com', 'Password123!')}
        data-testid="register-btn"
      >
        Register
      </button>
      <button onClick={logout} data-testid="logout-btn">
        Logout
      </button>
    </div>
  );
};

describe('AuthContext & Session Management', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('initializes as unauthenticated when no token is in storage', () => {
    render(
      <AuthProvider>
        <TestAuthConsumer />
      </AuthProvider>
    );

    expect(screen.getByTestId('auth-status').textContent).toBe('unauthenticated');
    expect(screen.getByTestId('user-role').textContent).toBe('none');
    expect(screen.getByTestId('token-val').textContent).toBe('none');
  });

  it('logs in successfully, updates user state, and persists token to sessionStorage', async () => {
    const mockAuthResponse = {
      token: 'jwt-admin-token-xyz',
      userId: 1,
      fullName: 'System Administrator',
      email: 'admin@resolveiq.com',
      role: 'Admin' as const,
    };

    vi.spyOn(authApi, 'login').mockResolvedValueOnce(mockAuthResponse);

    render(
      <AuthProvider>
        <TestAuthConsumer />
      </AuthProvider>
    );

    await act(async () => {
      screen.getByTestId('login-btn').click();
    });

    expect(screen.getByTestId('auth-status').textContent).toBe('authenticated');
    expect(screen.getByTestId('user-role').textContent).toBe('Admin');
    expect(screen.getByTestId('user-email').textContent).toBe('admin@resolveiq.com');
    expect(getStoredToken()).toBe('jwt-admin-token-xyz');
  });

  it('registers successfully and creates an Employee session', async () => {
    const mockAuthResponse = {
      token: 'jwt-employee-token-abc',
      userId: 4,
      fullName: 'Test User',
      email: 'new@resolveiq.com',
      role: 'Employee' as const,
    };

    vi.spyOn(authApi, 'register').mockResolvedValueOnce(mockAuthResponse);

    render(
      <AuthProvider>
        <TestAuthConsumer />
      </AuthProvider>
    );

    await act(async () => {
      screen.getByTestId('register-btn').click();
    });

    expect(screen.getByTestId('auth-status').textContent).toBe('authenticated');
    expect(screen.getByTestId('user-role').textContent).toBe('Employee');
    expect(getStoredToken()).toBe('jwt-employee-token-abc');
  });

  it('clears state and sessionStorage on logout', async () => {
    const mockAuthResponse = {
      token: 'jwt-token-to-clear',
      userId: 2,
      fullName: 'Support Agent',
      email: 'agent@resolveiq.com',
      role: 'Agent' as const,
    };

    vi.spyOn(authApi, 'login').mockResolvedValueOnce(mockAuthResponse);

    render(
      <AuthProvider>
        <TestAuthConsumer />
      </AuthProvider>
    );

    await act(async () => {
      screen.getByTestId('login-btn').click();
    });

    expect(screen.getByTestId('auth-status').textContent).toBe('authenticated');

    await act(async () => {
      screen.getByTestId('logout-btn').click();
    });

    expect(screen.getByTestId('auth-status').textContent).toBe('unauthenticated');
    expect(screen.getByTestId('user-role').textContent).toBe('none');
    expect(getStoredToken()).toBeNull();
  });
});
