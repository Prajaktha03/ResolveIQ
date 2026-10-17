import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { User, Role, AuthResponse } from '../types';
import { authApi, usersApi, getStoredToken, setStoredToken, ApiClientError } from '../services/api';

interface AuthContextType {
  user: User | null;
  token: string | null;
  role: Role | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  login: (email: string, password: string) => Promise<AuthResponse>;
  register: (fullName: string, email: string, password: string) => Promise<AuthResponse>;
  logout: () => void;
  clearError: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(getStoredToken());
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const clearError = useCallback(() => setError(null), []);

  const logout = useCallback(() => {
    setStoredToken(null);
    setToken(null);
    setUser(null);
    setError(null);
  }, []);

  // Hydrate user profile if token exists on application mount
  useEffect(() => {
    let isMounted = true;
    const initAuth = async () => {
      const storedToken = getStoredToken();
      if (!storedToken) {
        if (isMounted) setIsLoading(false);
        return;
      }

      try {
        const profile = await usersApi.getMe();
        if (isMounted) {
          setUser(profile);
          setToken(storedToken);
        }
      } catch (err: unknown) {
        if (err instanceof ApiClientError && err.isUnauthorized) {
          logout();
        } else {
          // If server is unreachable on refresh, preserve session or log out
          logout();
        }
      } finally {
        if (isMounted) setIsLoading(false);
      }
    };

    initAuth();
    return () => {
      isMounted = false;
    };
  }, [logout]);

  const login = async (email: string, password: string): Promise<AuthResponse> => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await authApi.login({ email, password });
      setStoredToken(response.token);
      setToken(response.token);
      setUser({
        id: response.userId,
        fullName: response.fullName,
        email: response.email,
        role: response.role,
        createdAt: new Date().toISOString(),
      });
      return response;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Login failed.';
      setError(msg);
      throw err;
    } finally {
      setIsLoading(false);
    }
  };

  const register = async (fullName: string, email: string, password: string): Promise<AuthResponse> => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await authApi.register({ fullName, email, password });
      setStoredToken(response.token);
      setToken(response.token);
      setUser({
        id: response.userId,
        fullName: response.fullName,
        email: response.email,
        role: response.role,
        createdAt: new Date().toISOString(),
      });
      return response;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Registration failed.';
      setError(msg);
      throw err;
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        token,
        role: user?.role || null,
        isAuthenticated: !!user && !!token,
        isLoading,
        error,
        login,
        register,
        logout,
        clearError,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
