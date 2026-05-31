# WellRyde user save (implemented in Tool Suite)

Captured May 2026 — **do not commit live session cookies.** Full chain: [wellryde-user-edit-chain.md](wellryde-user-edit-chain.md).

| Step | Method | URL |
|------|--------|-----|
| Load edit form | GET | `/portal/users/{SEC-ID}?form` → JSON (`referer: /portal/nu`, no XSRF headers) |
| Roles / companies | GET | `/portal/users/roles/selected?id={SEC}`, `companies/selected?id={SEC}` |
| Save | POST multipart | `/portal/users/nuUpdateUser` |
| Refresh | GET | `/portal/users/{SEC-ID}` |

Requires **`SESSION`** + `JSESSIONID` cookies. Key POST fields: `_csrf`, `selectedRolesUpdate`, `selectedCompaniesUpdate`, `address1`, `updatedFields`, org* mirrors.

Supey: **Edit driver** → **Save** pushes home address when the row has a WellRyde SEC id (same sign-in as billing).
