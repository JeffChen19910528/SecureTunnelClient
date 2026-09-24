# Configuration Format

## Status

- **Implemented & Tested & Passed:** the parsing and generation rules below, enforced by `SecureTunnel.Client.Infrastructure.Configuration.WireGuardConfigTextParser` and `SecureTunnel.Client.Infrastructure.WireGuard.TunnelFileTextBuilder`, covered by `WireGuardConfigTextParserTests` and `TunnelFileTextBuilderTests` (all passing - see `testing-guide.md` for the current count). Unchanged since Phase C2; Phase C3 built the connection engine and WPF import flow on top of this format without modifying it.

## Supported input

Standard WireGuard client configuration text with `[Interface]` and `[Peer]` sections, `Key = Value` lines, `#`/`;` comment lines, and blank lines. Example (full `[Interface]`+`[Peer]` document, as required for a usable client tunnel):

```ini
[Interface]
PrivateKey = <base64 private key>
Address = 10.0.0.2/32
DNS = 1.1.1.1
MTU = 1420

[Peer]
PublicKey = <base64 peer public key>
PresharedKey = <base64 preshared key, optional>
AllowedIPs = 10.10.0.0/24, 10.20.0.0/24
Endpoint = gateway.example.com:51820
PersistentKeepalive = 25
```

| Section | Key | Required | Notes |
|---|---|---|---|
| `[Interface]` | `PrivateKey` | Yes | Must be a 44-character base64 string (standard WireGuard key shape). Wrapped in `SensitiveString` immediately - never returned as a plain `string`. |
| `[Interface]` | `Address` | Yes | Must be a valid IP or CIDR. |
| `[Interface]` | `DNS` | No | Comma-separated IP list, surfaced on `ClientConfiguration.DnsServers`. |
| `[Interface]` | `MTU` | No | Positive integer, surfaced on `ClientConfiguration.Mtu`. |
| `[Interface]` | `ListenPort` | No | Recognized but not currently surfaced on `ClientConfiguration` (no client-side use case yet). |
| `[Peer]` | `PublicKey` | **Yes - mandatory for a usable client tunnel** | Same key-shape validation as `PrivateKey`. Stored on `ClientConfiguration.PeerPublicKey` - not secret (WireGuard public keys are not sensitive), but format-checked exactly like a private key. |
| `[Peer]` | `PresharedKey` | No | Same key-shape validation. **This IS secret** - wrapped in `SensitiveString`, surfaced as `ClientConfiguration.PresharedKey`, encrypted at rest alongside the private key, never logged. |
| `[Peer]` | `AllowedIPs` | **Yes** | Comma-separated list; every entry must be a valid IP/CIDR. **Never defaulted or widened by any code in this repository** - omitting it is a validation error, not an implicit `0.0.0.0/0`. |
| `[Peer]` | `Endpoint` | **Yes** | Must be a valid `host:port` (hostname, IPv4, or bracketed IPv6, plus a numeric port 1-65535), validated by `SecureTunnel.Client.Core.Validation.PeerEndpointValidator`. Surfaced on `ClientConfiguration.PeerEndpoint` - distinct from `ClientConfiguration.GatewayEndpoint`, which is product-level metadata about which Gateway issued the configuration. |
| `[Peer]` | `PersistentKeepalive` | No | Non-negative integer. |

## Rejected input

The parser returns `Success = false` with structured `ValidationIssue` entries (never a thrown exception for expected malformed input) for:

- Empty/whitespace-only text.
- A section header other than `[Interface]`/`[Peer]` (e.g. `[Unknown]`).
- Any line outside a section, or a line without an `=` separator.
- A missing `[Interface]` or `[Peer]` section.
- A missing, empty, or malformed `PrivateKey`, `PublicKey`, or `PresharedKey` (when present).
- An invalid `Address` or `AllowedIPs` entry, or a **missing** `AllowedIPs` (never implicitly filled in).
- A missing or malformed `Endpoint` (e.g. no port, out-of-range port, invalid host).
- A non-numeric or negative `PersistentKeepalive`, or a non-positive `MTU`.
- Duplicate keys within the same section (e.g. two `PrivateKey` lines).

An unsupported/unknown key within a recognized section (e.g. a future WireGuard option this client doesn't yet model) produces a **warning**, not an error - the configuration can still import, but the field is ignored rather than silently misinterpreted.

## Generation (`TunnelFileTextBuilder`)

`SecureTunnel.Client.Infrastructure.WireGuard.TunnelFileTextBuilder.Build(configuration, privateKey, presharedKey)` produces a complete, deterministic tunnel document:

- `[Interface]`: `PrivateKey`, `Address`, `DNS` (if present), `MTU` (if present) - in that fixed order.
- `[Peer]`: `PublicKey`, `PresharedKey` (if present), `AllowedIPs`, `Endpoint`, `PersistentKeepalive` (if present) - in that fixed order.

This is used by `WireGuardClientService.ConnectAsync` to build the short-lived temp tunnel file handed to `wireguard.exe`. `TunnelFileTextBuilderTests.Generate_RoundTrip_ProducesEquivalentDocument` parses a document, regenerates it, and re-parses the regenerated text, asserting the two `ClientConfiguration`s (and both keys) are semantically equivalent - proving the parser and generator agree on the format.

## Guarantees

- The parser never mutates the input string (verified by `Parse_NeverMutatesInputString`).
- The parser does not require WireGuard to be installed - it operates on text only.
- On any validation error, no `ClientConfiguration` or private/preshared key is produced (`ParsedConfigurationResult.Success = false`, `Configuration`, `PrivateKey`, and `PresharedKey` are all `null`) - the parser never returns a partially-populated result for invalid input.
- `TunnelFileTextBuilder` never logs its output (the returned string is plaintext key material by design, intended only for the short-lived, ACL-tight temp file `WireGuardClientService` writes it to).
