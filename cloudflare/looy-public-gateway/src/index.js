const DEFAULT_ORIGIN =
  "https://looy-admin-console.honest-crown-2664.chatgpt.site";
const BLOCKED_METHODS = new Set(["CONNECT", "TRACE"]);
const FORWARDED_HEADERS = [
  "x-looy-client-ip",
  "x-looy-client-city",
  "x-looy-client-region",
  "x-looy-client-country",
];

export default {
  async fetch(request, env) {
    if (BLOCKED_METHODS.has(request.method.toUpperCase())) {
      return new Response("Method not allowed", { status: 405 });
    }

    const publicUrl = new URL(request.url);
    if (publicUrl.pathname === "/__gateway/health") {
      return Response.json(
        { ok: true, service: "looy-public-gateway", version: 1 },
        { headers: { "cache-control": "no-store" } },
      );
    }

    const origin = validatedOrigin(env.ORIGIN_URL);
    const upstreamUrl = new URL(publicUrl.pathname + publicUrl.search, origin);
    const headers = createUpstreamHeaders(request, publicUrl.origin, origin);
    const init = {
      method: request.method,
      headers,
      redirect: "manual",
    };
    if (request.method !== "GET" && request.method !== "HEAD") {
      init.body = request.body;
    }

    try {
      const upstream = await fetch(new Request(upstreamUrl, init));
      return createPublicResponse(upstream, publicUrl.origin, origin);
    } catch (error) {
      console.error("looy gateway upstream failure", {
        path: publicUrl.pathname,
        message: error instanceof Error ? error.message : "unknown",
      });
      return Response.json(
        {
          ok: false,
          error: "gateway_unavailable",
          message: "路遥智伴服务暂时无法连接，请稍后重试。",
        },
        {
          status: 502,
          headers: { "cache-control": "no-store" },
        },
      );
    }
  },
};

export function validatedOrigin(value) {
  const url = new URL(value || DEFAULT_ORIGIN);
  if (url.protocol !== "https:" || url.username || url.password) {
    throw new Error("ORIGIN_URL must be an HTTPS origin");
  }
  return url.origin;
}

export function createUpstreamHeaders(request, publicOrigin, origin) {
  const headers = new Headers(request.headers);
  headers.delete("host");
  headers.delete("x-forwarded-for");
  headers.delete("x-forwarded-host");
  headers.delete("x-forwarded-proto");
  for (const name of FORWARDED_HEADERS) headers.delete(name);

  const requestOrigin = headers.get("origin");
  if (requestOrigin === publicOrigin) headers.set("origin", origin);

  const referer = headers.get("referer");
  if (referer?.startsWith(`${publicOrigin}/`)) {
    headers.set("referer", `${origin}${referer.slice(publicOrigin.length)}`);
  }

  setMetadataHeader(
    headers,
    "x-looy-client-ip",
    request.headers.get("cf-connecting-ip"),
  );
  setMetadataHeader(
    headers,
    "x-looy-client-city",
    request.cf?.city,
  );
  setMetadataHeader(
    headers,
    "x-looy-client-region",
    request.cf?.region,
  );
  setMetadataHeader(
    headers,
    "x-looy-client-country",
    request.cf?.country,
  );
  headers.set("x-looy-gateway-version", "1");
  return headers;
}

export function createPublicResponse(upstream, publicOrigin, origin) {
  const headers = new Headers(upstream.headers);
  const location = headers.get("location");
  if (location) {
    const target = new URL(location, origin);
    if (target.origin === origin) {
      headers.set(
        "location",
        `${publicOrigin}${target.pathname}${target.search}${target.hash}`,
      );
    }
  }

  const getSetCookie = upstream.headers.getSetCookie?.bind(upstream.headers);
  if (getSetCookie) {
    const cookies = getSetCookie();
    headers.delete("set-cookie");
    for (const cookie of cookies) {
      headers.append("set-cookie", withoutOriginDomain(cookie));
    }
  }

  headers.set("x-looy-gateway", "workers.dev");
  headers.delete("content-length");
  return new Response(upstream.body, {
    status: upstream.status,
    statusText: upstream.statusText,
    headers,
  });
}

function setMetadataHeader(headers, name, value) {
  if (typeof value !== "string") return;
  const safe = value.trim().replace(/[\r\n]/g, "").slice(0, 80);
  if (safe) headers.set(name, safe);
}

function withoutOriginDomain(cookie) {
  return cookie.replace(/;\s*Domain=\.?chatgpt\.site(?=;|$)/gi, "");
}
