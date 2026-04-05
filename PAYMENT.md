# RunFence — Licensing & Payment

**[Documentation / README](https://github.com/runfence/RunFence#readme)**

## Why Paid?

RunFence is a complex security product. Building it involves deep research into Windows internals, careful threat modeling, and a level of attention to correctness that isn't optional when the whole point is isolation and access control. Every feature goes through multiple rounds of design, implementation, testing, and revision. Edge cases get tracked down. Regressions get caught and fixed. Low-quality code gets rewritten, not shipped.

AI tools are part of the workflow, but they lower the bar for the first draft, not the final one. Code that compiles and runs is not the same as code that is correct, maintainable, and secure — and closing that gap takes real work. Initial implementations get reviewed, pulled apart, and rewritten until the quality is actually there. On top of that, someone has to know what to build, decide how it should work, verify that it actually works, and maintain it over time. That work is ongoing and it adds up.

A paid license is how that work stays sustainable.

If RunFence has become essential to your workflow, purchasing a license is the best way to support its ongoing development and help ensure it continues to improve.

## Pricing

### Individual Licenses

| Tier | Price | Validity |
|------|-------|----------|
| Quarterly | $49 | 3 months from purchase date |
| Annual | $99 | 1 year from purchase date |
| Lifetime | $249 | Perpetual for current major version |
| Contributor | Free | Variable (based on contribution scope) |

**Why no monthly tier?** Payments are processed manually, so we currently don't have an automatic subscription system. If you need shorter-term licensing, the Quarterly tier ($49/3 months) offers the most flexibility.

All licenses are machine-bound (identified by your **Machine Code**, shown in the app).
The Machine Code is tied to your hardware, not a specific Windows installation.

**Reinstalls & Dual Boot:** Your license survives Windows reinstalls on the same machine — you will not lose it if you reinstall Windows or RunFence. Dual-boot setups on the same hardware share the same Machine Code, so a single license covers all Windows installations on that machine.

Minor and patch updates are free; major version upgrades require a new key.

### Open Source Contributors

We offer free licenses to contributors based on the scope of their work:

| Tier | Free License | Example Scope |
|------|---|---|
| Contributor | 3 months | Noticeable PRs (≈1 day equivalent work) |
| Major Contributor | 1 year | Significant contributions (≈5+ days equivalent work) |
| Core Contributor | Lifetime | Substantial effort (≈2 weeks+ equivalent work) |

All contributor licenses are machine-bound (identified by your **Machine Code**).

**How it works:**
- Work scope is judged by PR content at the author's discretion
- You can split work across multiple PRs — total contribution will be evaluated
- After a free license expires, you can earn renewal by submitting another recent PR
- Lifetime contributors never need renewal
- Contribute more to earn more keys (one key per machine)

To claim your license, include your **Machine Code** and email in the PR description or a comment. Once merged and evaluated, you will receive your license key by email.

### Supporters

Voluntary donations are welcome. Donors of $500 or more are added to
the public donors list unless they prefer to remain anonymous.

---

## How to Purchase

1. **Find your Machine Code** — open RunFence → About tab (or the evaluation nag screen). It looks like `ABCDE-FGHIJ-KLMNO-PQRST`.
2. **Send payment** using one of the crypto addresses below.
3. **Email us** at **runfencedev@gmail.com** with:
   - Your Machine Code
   - Your full name or organization name (this is embedded in your license key)
   - Desired tier (Quarterly / Annual / Lifetime)
   - Payment transaction ID / Hash and Date
4. **Receive your license key** by email, typically within 24 hours.
5. **Enter the key** in RunFence → About tab → "Enter License Key…"

---

## Don't Have Crypto Yet?

The easiest path is a wallet app with a built-in purchase feature. Install one, use the built-in **Buy** option to purchase with a card, then **Send** to the address below:

- **[Trust Wallet](https://trustwallet.com/)** (mobile)
- **[Exodus](https://www.exodus.com/)** (desktop + mobile)
- **[Atomic Wallet](https://atomicwallet.io/)** (desktop + mobile)

Alternatively, **[BestChange.com](https://www.bestchange.com/)** aggregates instant exchangers — filter by your payment method (card, bank transfer, cash, etc.) and send crypto directly to the address below without setting up a wallet.

---

## Accepted Payment Methods

### Monero (XMR)

```
875RoqziXcRSajgNzaGocvMLUEnpWfjkLS3vCEqNhGmFfJ8FMnhorcF93Bv7hJ55q4fb2MKDSUZkm2LZ6PUYYkua6KTMDs7
```

### Bitcoin (BTC) (silent)

```
sp1qq0k0tc8kszqeh7tjjwkvl7ujnztf0505uhrj9nxlw8ynr76pvdgs2q5tkx8rt39qphfmatzckz6rzfdkrlpnqn6juj4qqm43d2a863x8fya42sry
```

### Bitcoin (BTC) (legacy)

```
bc1qnhllce0cjdwz6vmnt6p2zr24gp2xyunc4ptpqy
```

### Bitcoin Cash (BCH)

```
qz7qg9wj2tfnreeg96vhwmz4ke760y064shluf6nyp
```

### Ethereum (ETH / USDT-ERC20/USDC-ERC20)

```
0x6F647dB2B7fB65726e0b9B963b9FB4d50336b9FA
```

### Tron (USDT-TRC20/USDC-TRC20)

```
TX6FYDgkc377RyJB4tjbveSq9cye317tay
```


> **Note:** Crypto wallet addresses are published here on GitHub only. They are **not** embedded in the application binary. Always verify you are reading this file from the official repository at https://github.com/runfence/RunFence before sending funds.

---

## Contact

**Email:** runfencedev@gmail.com
**GitHub:** https://github.com/runfence/RunFence

For support, license transfers, or refund requests, please email with your license key and order details.

---

## Verifying the Licensing System

RunFence uses offline ECDsa P-256 license keys. The signing public key is embedded in
the binary. To detect whether the licensing system has been tampered with, compare the
**Licensing Key Fingerprint** shown in About → License section against the official
fingerprint:

```
39:59:B7:0A:23:39:29:93:06:D3:98:84:E3:55:37:4E
```

A mismatch means the embedded key was tampered with — you are likely running a cracked
build, which is illegal. Do not trust this binary.

---

## License Terms

RunFence is source-available under the [Elastic License 2.0](LICENSE.md). Evaluation mode is available for free use with limitations.
