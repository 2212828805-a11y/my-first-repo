import assert from "node:assert/strict";
import test from "node:test";
import {
  createPublicResponse,
  createUpstreamHeaders,
  validatedOrigin,
} from "../src/index.js";

test("rewrites same-origin mutation headers for the fixed upstream", () => {
  const request = new Request("https://looy.example.workers.dev/api/auth/login", {
    method: "POST",
    headers: {
      origin: "https://looy.example.workers.dev",
      referer: "https://looy.example.workers.dev/admin",
      "cf-connecting-ip": "203.0.113.9",
      "x-looy-client-ip": "spoofed",
    },
  });
  Object.defineProperty(request, "cf", {
    value: { city: "Hangzhou", region: "Zhejiang", country: "CN" },
  });

  const headers = createUpstreamHeaders(
    request,
    "https://looy.example.workers.dev",
    "https://origin.example",
  );
  assert.equal(headers.get("origin"), "https://origin.example");
  assert.equal(headers.get("referer"), "https://origin.example/admin");
  assert.equal(headers.get("x-looy-client-ip"), "203.0.113.9");
  assert.equal(headers.get("x-looy-client-city"), "Hangzhou");
});

test("does not rewrite a cross-site origin", () => {
  const request = new Request("https://looy.example.workers.dev/api/auth/login", {
    headers: { origin: "https://attacker.example" },
  });
  const headers = createUpstreamHeaders(
    request,
    "https://looy.example.workers.dev",
    "https://origin.example",
  );
  assert.equal(headers.get("origin"), "https://attacker.example");
});

test("rewrites upstream redirects and cookie domains", async () => {
  const upstream = new Response(null, {
    status: 303,
    headers: {
      location: "https://origin.example/admin",
      "set-cookie": "looy_admin_session=token; Path=/; Domain=chatgpt.site; Secure",
    },
  });
  const response = createPublicResponse(
    upstream,
    "https://looy.example.workers.dev",
    "https://origin.example",
  );
  assert.equal(
    response.headers.get("location"),
    "https://looy.example.workers.dev/admin",
  );
  assert.doesNotMatch(response.headers.get("set-cookie"), /Domain=/i);
});

test("rejects non-HTTPS origins", () => {
  assert.throws(() => validatedOrigin("http://example.com"), /HTTPS/);
});
