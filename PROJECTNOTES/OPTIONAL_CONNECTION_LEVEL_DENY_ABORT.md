# Raw idea: optional connection-level deny abort

## Status

Raw idea only. No implementation approach or public API has been selected.

## Context

The current deny enforcement path is an HTTP pipeline short-circuit:

1. A request is evaluated as blocked.
2. The middleware writes the configured HTTP status code and response body.
3. The middleware returns without invoking the next request delegate.

This is a request-level rejection. TCP and TLS have already been established, an HTTP response is returned, and the underlying connection may remain reusable.

## Idea

Consider adding a second, optional enforcement path for deny decisions: abort the underlying client connection instead of producing an HTTP error response.

For IP-based denial, the strongest form of this idea would run at the Kestrel connection layer before HTTPS/TLS handling. A denied client would then observe a reset, close, or TLS handshake failure rather than a normal HTTP response.

The two paths would serve different purposes:

- **HTTP short-circuit:** return a controlled status code and response body.
- **Connection-level abort:** terminate the connection without an application-level HTTP response.

The connection-level path should be considered an additional enforcement option, not necessarily a replacement for the existing short-circuit behavior.

## Important boundary

Before TLS and HTTP parsing, only connection-level information is available. In practice, this makes the early path primarily suitable for decisions already known from the remote endpoint, especially the source IP address. Rules depending on headers, URL, method, protocol metadata exposed at HTTP level, signatures, or other request content cannot be evaluated at that stage.

## Open implementation questions

These questions are intentionally left unresolved:

- Where should the denied-IP state live so that Kestrel connection middleware can read it safely and efficiently?
- How should evaluator updates become visible to already configured listeners?
- Should the observable behavior be a reset, graceful close, or another transport-level termination?
- How should IPv4-mapped IPv6 addresses and address normalization be handled?
- What should happen when a reverse proxy is in front of Kestrel and the connection address is the proxy address?
- Should connection-level abort be global, endpoint-specific, or independently configurable per listener?
- How should the existing HTTP short-circuit and the new connection-level path be named and selected?
- Which tests are needed to distinguish TCP connect success, TLS handshake failure, HTTP rejection, and connection reuse behavior?

## Non-goals of this note

This note does not define the implementation, configuration schema, service interfaces, synchronization mechanism, or default behavior. It only records the possibility of a second deny enforcement path below the HTTP request pipeline.
