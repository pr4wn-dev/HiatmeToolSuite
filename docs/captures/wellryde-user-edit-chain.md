# WellRyde user edit / save chain (browser capture, May 2026)

Redact cookies before committing. Structural reference for Tool Suite `WellRydeUserProfileSync`.

## Cookies (all XHR)

`SESSION`, `JSESSIONID`, `AWSALB`, `AWSALBCORS` — **`SESSION` is required** for JSON from user admin APIs.

## Edit existing user (open profile → save)

| Order | Method | URL |
|------|--------|-----|
| 1 | GET | `/portal/users/{SEC}?form` — **no** `X-Requested-With`, **no** `X-XSRF-TOKEN`; `referer: /portal/nu` |
| 2+ | GET | `/portal/users/roles/selected?id={SEC}` (and available, companies selected/available, groups, assignmentTypes) — `x-requested-with: XMLHttpRequest` |
| save | POST multipart | `/portal/users/nuUpdateUser` — `_csrf` in body only; `referer: /portal/nu` |

## Save POST must include (from capture)

- `selectedRolesUpdate` — e.g. `3,5,7,9,11,13,15,17,22,25,28,30,31,43` (from roles/selected)
- `selectedCompaniesUpdate` — e.g. `SEC-bZthm7L1nC9qhPnShGRhYw`
- `updatedFields` — includes `Address1`, `City`, `State`, etc.
- All `org*` mirror fields from form JSON

## Not in save path

- `/portal/msgctr/getmessagecount`
- `/portal/avl/avlinitiate`
- `user.js` script load
