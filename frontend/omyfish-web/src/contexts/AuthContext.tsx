"use client";

import { createContext, useContext, useEffect, useState, useCallback, ReactNode } from "react";
import { api, TokenResponse } from "@/lib/api";

interface AuthState {
  token: string | null;
  userId: string | null;
  email: string | null;
}

interface AuthContextValue extends AuthState {
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  isAuthenticated: boolean;
  isLoading: boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState>({ token: null, userId: null, email: null });
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    const token = localStorage.getItem("omyfish_token");
    const userId = localStorage.getItem("omyfish_userId");
    const email = localStorage.getItem("omyfish_email");
    const refreshToken = localStorage.getItem("omyfish_refresh");
    if (token) {
      setAuth({ token, userId, email });
      setIsLoading(false);
    } else if (refreshToken) {
      api.auth.refresh(refreshToken)
        .then((resp) => {
          persistAuth(resp);
          setAuth({ token: resp.token, userId: resp.userId, email: resp.email });
        })
        .catch(() => clearStorage())
        .finally(() => setIsLoading(false));
    } else {
      setIsLoading(false);
    }
  }, []);

  const persistAuth = (resp: TokenResponse) => {
    localStorage.setItem("omyfish_token", resp.token);
    localStorage.setItem("omyfish_refresh", resp.refreshToken);
    localStorage.setItem("omyfish_userId", resp.userId);
    localStorage.setItem("omyfish_email", resp.email);
  };

  const clearStorage = () => {
    localStorage.removeItem("omyfish_token");
    localStorage.removeItem("omyfish_refresh");
    localStorage.removeItem("omyfish_userId");
    localStorage.removeItem("omyfish_email");
  };

  const login = useCallback(async (email: string, password: string) => {
    const resp: TokenResponse = await api.auth.login(email, password);
    persistAuth(resp);
    setAuth({ token: resp.token, userId: resp.userId, email: resp.email });
  }, []);

  const logout = useCallback(() => {
    clearStorage();
    setAuth({ token: null, userId: null, email: null });
  }, []);

  return (
    <AuthContext.Provider value={{ ...auth, login, logout, isAuthenticated: !!auth.token, isLoading }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
