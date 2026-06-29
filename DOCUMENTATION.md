# SSG Finance — Project Documentation

---

## 1. Project Overview

SSG Finance is a web application for managing **Supreme Student Government (SSG) finances** at CTU Ginatilan. It tracks organizational fee payments from students, records expenses, manages other funds, and generates financial reports. The system is accessed through a school local area network (LAN) by five user roles: Admin, Treasurer, Professor, Advisor, and Student.

**Tech Stack:**

- **Backend:** ASP.NET Core MVC (.NET 10), Entity Framework Core with MySQL
- **Frontend:** Razor views (`.cshtml`) with inline CSS and JavaScript, Chart.js, jsPDF
- **Database:** MySQL 8.0
- **Security:** BCrypt password hashing, session-based authentication, CSRF token protection
- **Real-time:** Server-Sent Events (SSE)

---

## 2. How to Run the Project

### Local Development

1. `dotnet restore`
2. Update `appsettings.json` with your MySQL connection string
3. `dotnet run`
4. App starts at `https://localhost:5183`
5. Default admin: `admin@ssg.com` / `admin123`

### Docker

1. Copy `.env.example` to `.env`, fill in secrets
2. `bash deploy.sh`
3. Access at `http://<server-ip>:8085`

### Environment Variables

| Variable | Required | Default | Purpose |
|----------|----------|---------|---------|
| `APP_PORT` | No | `8085` | Application HTTP port |
| `MYSQL_HOST` | Yes | `mysql` | MySQL server hostname (use `db` in Docker) |
| `MYSQL_PORT` | No | `3306` | MySQL server port |
| `MYSQL_DATABASE` | No | `ssg_system` | Database name |
| `MYSQL_ROOT_PASSWORD` | Yes | — | MySQL root password (Docker initialization) |
| `MYSQL_USER` | Yes | — | Application database user |
| `MYSQL_PASSWORD` | Yes | — | Application database password |
| `SMTP_HOST` | No | — | SMTP server host (email disabled if empty) |
| `SMTP_PORT` | No | — | SMTP server port |
| `SMTP_USERNAME` | No | — | SMTP username / sender email |
| `SMTP_PASSWORD` | No | — | SMTP password |
| `SMTP_ENABLE_SSL` | No | `true` | Enable SSL for SMTP |

---

## 3. Project Structure

```
SSG_Finance/
├── Program.cs                        # Entry point, middleware, DI registration
├── MyMvcApp.csproj                   # .NET 10 project (EF Core, BCrypt, Pomelo MySQL)
├── appsettings.json                  # Config (connection string, logging)
├── Dockerfile / docker-compose.yml   # Container setup
├── deploy.sh                         # Deployment script
│
├── Controllers/                      # MVC controllers (partial classes for HomeController)
│   ├── AppController.cs              # Base: auth guards, login lockout
│   ├── HomeController.cs             # Login, logout, dashboards, SSE stream
│   ├── HomeController.Accounts.cs    # Registration, approvals, profiles, password reset
│   ├── HomeController.Fees.cs        # School years, courses, fee amounts, exemptions
│   ├── HomeController.PaymentLookups.cs  # Fee status queries
│   ├── HomeController.Reports.cs     # Financial report CRUD
│   ├── HomeController.Signatures.cs  # Treasurer signatures, OTP, receipts
│   ├── HomeController.Transactions.cs    # Payment/fund/expense data retrieval
│   ├── HomeController.TransactionWrites.cs  # Payment/fund/expense CRUD
│   └── HomeControllerModels.cs       # Request/response DTOs
│
├── Models/                           # 25 entity and view model files
├── Data/
│   ├── ApplicationDbContext.cs       # EF Core context (15 DbSets)
│   └── ApplicationDbContextSeed.cs   # Seeds default admin account
│
├── Services/
│   ├── AuthService.cs               # Login, registration, BCrypt hashing
│   ├── EmailService.cs              # SMTP email sending
│   ├── FeeRules.cs                  # Fee applicability business rules
│   └── SseService.cs                # Server-Sent Events broadcast
│
├── Views/
│   ├── Dashboard/                   # 5 role-specific dashboards (admin, treasurer, etc.)
│   ├── Home/                        # Landing page, about, contacts, logout
│   └── Shared/                      # Developer modal partial
│
└── wwwroot/
    ├── js/csrf.js                   # CSRF token auto-attachment
    ├── images/                      # Logos, campus photo
    └── lib/                         # Bootstrap, jQuery (vendored)
```

---

## 4. Core Features

### 4.1 User Management

- **Registration:** Students, professors, and advisors can register with their CTU ID, email, and password. Students must provide course, year level, and section.
- **Approval workflow:** New accounts start as "Pending". Admins approve or reject requests. Email notifications are sent.
- **Role-based access:** Five roles — Admin, Treasurer, Professor, Advisor, Student — each with specific permissions.
- **Password reset:** Forgot password flow with 6-digit email verification code and 15-minute expiry.
- **Profile management:** Avatar upload, email update, password change.

### 4.2 Organizational Fee Management

- **School years:** Admins/Treasurers create school years with semester date ranges. Only one school year can be "Current" at a time.
- **Fee amounts:** Set fee amounts per semester per school year. Only one semester can be "Current" at a time.
- **Courses:** Manage course codes (e.g., BSIT, BSCS).
- **Student year-level promotion:** When a new school year is added, enrolled students' year levels advance automatically (up to level 5).
- **Fee exemptions:** Treasurer/Admin can exempt specific students from specific semesters (e.g., for leave of absence).

### 4.3 Payment Processing

- **Record payments:** Treasurer/Admin records a student's org fee payment. The system creates both a payment record and a receipt in one transaction.
- **Payment status:** Students are "Paid" (full amount covered), "Partial" (some paid), or "Unpaid".
- **Prior-year block:** A student cannot pay for a new school year until all applicable previous-year fees are fully settled.
- **Semester ordering:** 2nd semester cannot be paid until 1st semester is fully paid.
- **Edit window:** Payments can only be edited or deleted within 15 minutes of creation.

### 4.4 Other Funds & Expenses

- **Other funds:** Record non-org-fee income (e.g., donations, sponsorships). Auto-matches to a school year based on date.
- **Expenses:** Record expenses with optional receipt images (JPG/PNG/WEBP, max 5MB each). The system enforces chronological balance protection — expenses cannot make the running balance negative on any date.

### 4.5 Financial Reports

- **Report creation:** Treasurers select specific funds and expenses to include. The system computes beginning balance, total revenue, total expenses, and running balance.
- **Reports are read-only** once created and can be deleted by Treasurers/Admins.

### 4.6 Receipts

- **Auto-generated:** Each payment record gets a unique receipt number in format `OR-YYYY-NNN`.
- **Treasurer signatures:** Treasurers can save a digital signature (PNG base64) that appears on receipts.
- **PDF and print:** Receipts can be printed or exported as PDF via jsPDF + html2canvas.

### 4.7 Real-Time Updates

- **Server-Sent Events (SSE):** The treasurer and other dashboards receive live updates when payments, accounts, fees, or school years change.
- **Event types:** `accounts-changed`, `school-years-changed`, `courses-changed`, `fees-changed`, `payments-changed`.

### 4.8 Role-Specific Dashboards

- **Admin:** Manage accounts (approve/reject), view students, professors, and treasurers, financial overview with charts.
- **Treasurer:** Full financial management — payments, expenses, funds, reports, receipts, signatures.
- **Professor:** Read-only view of student payment status.
- **Advisor:** Financial oversight and student payment tracking.
- **Student:** View personal fee status, payment history, and receipts.

---

## 5. Data Flow

### Login Flow

1. User enters CTU ID and password on the landing page.
2. Frontend sends `POST /Home/Login` with JSON `{schoolId, password}`.
3. `HomeController.Login` validates credentials via `AuthService.AuthenticateBySchoolIdAsync`.
4. On success, session variables are set (role, name, school ID, avatar path, etc.) and the user is redirected to their role-specific dashboard.
5. On failure, failed attempts are tracked. After 5 failures, a 15-minute lockout is enforced.

### Payment Flow

1. Treasurer selects a student and a fee (school year + semester).
2. Frontend calls `GET /Home/GetStudentFeeStatus` to check eligibility and balance.
3. If eligible, treasurer enters the payment amount and submits `POST /Home/AddOrgFeePayment`.
4. Backend validates: fee applicability, prior-year balance rule, semester ordering, no duplicate payment.
5. Payment and receipt are created in a single database transaction.
6. SSE broadcasts `payments-changed` to all connected clients.
7. The treasurer dashboard updates in real-time.

### Account Registration Flow

1. User fills the registration form (CTU ID, name, email, password, course/section for students).
2. Frontend sends `POST /Home/Register`.
3. Backend validates: unique CTU ID and email, password policy, creates Account (Pending status), User, and AcademicProfile (for students).
4. Admin receives the request in the dashboard and can approve or reject.
5. On approval, an email notification is sent and SSE broadcasts `accounts-changed`.

### Fee Applicability Rules (`FeeRules.cs`)

1. Students who are Dropped or at year level 5 (graduated) owe nothing.
2. Students with a fee exemption for a specific semester are not charged for that semester.
3. Students only pay from their enrollment semester forward — they are not charged for semesters before they joined.

---

## 6. Key Components

### Controllers

| File | Responsibility |
|------|---------------|
| `AppController.cs` | Base controller with auth guards, login lockout, session helpers |
| `HomeController.cs` | Login/logout, dashboard routing, SSE event stream |
| `HomeController.Accounts.cs` | Registration, approval, profile management, password reset |
| `HomeController.Fees.cs` | School years, courses, fee amounts, exemptions, password change |
| `HomeController.PaymentLookups.cs` | Fee status queries, collectable amount calculations |
| `HomeController.Reports.cs` | Financial report CRUD |
| `HomeController.Signatures.cs` | Treasurer signatures, email OTP, receipt numbering, fee editing |
| `HomeController.Transactions.cs` | Payment/fund/expense data retrieval, dashboard statistics |
| `HomeController.TransactionWrites.cs` | Payment/fund/expense CRUD with 15-minute edit window and balance validation |

### Services

| File | Responsibility |
|------|---------------|
| `AuthService.cs` | Authentication, registration, BCrypt password hashing |
| `EmailService.cs` | SMTP email sending (password reset, notifications) |
| `FeeRules.cs` | Static business logic for fee applicability |
| `SseService.cs` | Server-Sent Events subscription and broadcast |

### Data Layer

| File | Responsibility |
|------|---------------|
| `ApplicationDbContext.cs` | EF Core context with 15 DbSets and Fluent API configuration |
| `ApplicationDbContextSeed.cs` | Seeds default admin account (`admin@ssg.com` / `admin123`) |

### Models (Key Entities)

| Entity | Purpose |
|--------|---------|
| `Account` | Login credentials, role, approval status |
| `User` | Personal info (name, avatar), linked 1:1 to Account |
| `AcademicProfile` | Student academic info (course, year level, section) |
| `SchoolYear` | Academic year with semester date ranges |
| `FullAmount` | Fee amount per semester per school year |
| `OrgFeePayment` | Payment record linking student, fee, and receiver |
| `OtherFund` | Non-fee income record |
| `Expense` | Expense record with optional images |
| `Receipt` | Official receipt for a payment |
| `Report` | Financial report with line items |
| `StudentFeeExemption` | Fee exemption for a student per semester |

---

## 7. External Services

| Service | Purpose | Configuration |
|---------|---------|---------------|
| **MySQL 8.0** | Primary database | `ConnectionStrings:DefaultConnection` via environment variables or `appsettings.json` |
| **SMTP (Gmail)** | Password reset and notification emails | `SmtpSettings__*` environment variables (Docker) or `SmtpSettings` section in `appsettings.json` |
| **Google Fonts** | Inter, DM Sans, DM Mono, Libre Baskerville fonts | CDN links in views |
| **Ionicons 5.5.2** | Icon library | CDN link in views |
| **Chart.js** | Dashboard charts (monthly overview, financial summaries) | CDN link in views |
| **jsPDF + html2canvas** | PDF receipt generation | CDN link in views |

---

## 8. Notes

### Security Considerations

- The default admin account (`admin@ssg.com` / `admin123`) should be changed after first deployment.
- Password policy: minimum 8 characters, no spaces, must include uppercase, lowercase, digit, and special character.
- CSRF protection is enforced on all POST/PUT/DELETE/PATCH requests via `AutoValidateAntiforgeryToken` and the `csrf.js` frontend wrapper.
- HTML pages are set to `no-store, no-cache` to prevent back-button data exposure after logout.
- Security headers (`X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`) are added via middleware.

### Code Architecture Observations

- The application does **not** use a shared `_Layout.cshtml`. Each view is a self-contained full HTML document, leading to significant CSS/JS duplication across dashboards.
- All business logic JavaScript is inline within `.cshtml` files (up to 6200 lines for the treasurer dashboard). There are no separate JS module files.
- The `wwwroot/css/dashboard.css` file contains a light-themed dashboard design that appears to be a legacy/alternative version not currently used by the dark-themed dashboard views.
- The `wwwroot/js/site.js` file is a placeholder and is unused.

### Deployment Notes

- **Docker deployment** binds to port 8085 (configurable via `APP_PORT` in `.env`).
- The native deployment uses HTTP only (no HTTPS). This works on the school LAN because browsers ignore HSTS for raw IP addresses.
- If adding Nginx as a reverse proxy later, disable `proxy_buffering` for the SSE endpoint (`/Home/Events`).
- Database backups: `mysqldump ssg_system > ssg_$(date +%F).sql`
- Upload backups: `tar czf uploads_$(date +%F).tgz /opt/ssg/wwwroot/uploads`
