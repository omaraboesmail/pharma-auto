# Admin Portal

Next.js admin surface لإدارة tenants،subscriptions،Connector health،usage،certificates وsecurity incidents.

## Security Rules

- Supabase Auth مع mandatory TOTP MFA.
- backend enforcement لـ `aal2`.
- RBAC حسب الوظيفة.
- no raw invoice access افتراضيًا.
- break-glass مؤقت ومدقق.
- لا Supabase service role أوSaaS secrets داخل browser.
