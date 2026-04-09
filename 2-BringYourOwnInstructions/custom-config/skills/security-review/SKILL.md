---
name: security-review
description: Performs OWASP Top 10 security analysis on code
---

# Security Review Skill

When asked to review code, always check for these security concerns:

1. **Injection** — SQL injection, command injection, XSS
2. **Broken Auth** — Hardcoded credentials, weak token validation
3. **Sensitive Data** — Secrets in source, missing encryption
4. **Broken Access Control** — Missing authorization checks, IDOR
5. **Misconfig** — Debug mode in prod, overly permissive CORS
6. **Vulnerable Dependencies** — Known CVEs in packages

Format findings as: `[SEVERITY] Description (file:line)`
