import { describe, it, expect, vi, beforeEach } from "vitest";
import { api, setAuthHandlers, TokenResponse } from "./api";

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as Response;
}

const REFRESHED: TokenResponse = {
  token: "new-token",
  userId: "u1",
  email: "a@b.com",
  role: "USER",
};

describe("apiFetch 401 handling", () => {
  beforeEach(() => {
    setAuthHandlers({});
  });

  it("passes through a successful request unchanged", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([{ id: "1" }]));
    vi.stubGlobal("fetch", fetchMock);

    const result = await api.observations.getAll("good-token");

    expect(result).toEqual([{ id: "1" }]);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("silently refreshes and retries once on a 401, syncing auth state", async () => {
    const onTokenRefreshed = vi.fn();
    setAuthHandlers({ onTokenRefreshed });

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ error: "expired" }, 401)) // original call
      .mockResolvedValueOnce(jsonResponse(REFRESHED)) // /auth/refresh
      .mockResolvedValueOnce(jsonResponse([{ id: "obs-1" }])); // retry
    vi.stubGlobal("fetch", fetchMock);

    const result = await api.observations.getAll("expired-token");

    expect(result).toEqual([{ id: "obs-1" }]);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1][0]).toContain("/api/v1/auth/refresh");
    expect(fetchMock.mock.calls[2][1]).toMatchObject({
      headers: { Authorization: "Bearer new-token" },
    });
    expect(onTokenRefreshed).toHaveBeenCalledWith(REFRESHED);
  });

  it("signals session expiry and rejects when the refresh cookie is also dead", async () => {
    const onSessionExpired = vi.fn();
    setAuthHandlers({ onSessionExpired });

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ error: "expired" }, 401)) // original call
      .mockResolvedValueOnce(jsonResponse({ error: "no cookie" }, 401)); // refresh fails
    vi.stubGlobal("fetch", fetchMock);

    await expect(api.observations.getAll("expired-token")).rejects.toThrow("401");
    expect(onSessionExpired).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2); // no retry attempted
  });

  it("dedupes concurrent refresh attempts into a single /auth/refresh call", async () => {
    const seenOnce = new Set<string>();
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      if (url.includes("/api/v1/auth/refresh")) {
        return Promise.resolve(jsonResponse(REFRESHED));
      }
      if (seenOnce.has(url)) {
        return Promise.resolve(jsonResponse({ url }));
      }
      seenOnce.add(url);
      return Promise.resolve(jsonResponse({ error: "expired" }, 401));
    });
    vi.stubGlobal("fetch", fetchMock);

    const [a, b] = await Promise.all([
      api.observations.getById("1", "expired-token"),
      api.observations.getById("2", "expired-token"),
    ]);

    expect(a).toMatchObject({ url: expect.stringContaining("/observations/1") });
    expect(b).toMatchObject({ url: expect.stringContaining("/observations/2") });

    const refreshCalls = fetchMock.mock.calls.filter(([url]) =>
      (url as string).includes("/api/v1/auth/refresh")
    );
    expect(refreshCalls).toHaveLength(1);
  });
});
