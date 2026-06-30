using Microsoft.EntityFrameworkCore.Migrations;
using MyMvcApp.Models;

#nullable disable

namespace MyMvcApp.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    account_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    school_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    roles = table.Column<string>(type: "longtext", nullable: false),
                    request_status = table.Column<string>(type: "longtext", nullable: false, defaultValue: "Pending"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    password_reset_token = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    password_reset_token_expires = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    course_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    course_code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    course_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.course_id);
                });

            migrationBuilder.CreateTable(
                name: "school_years",
                columns: table => new
                {
                    school_year_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    year_start = table.Column<int>(type: "int", nullable: false),
                    year_end = table.Column<int>(type: "int", nullable: false),
                    year_status = table.Column<string>(type: "longtext", nullable: false, defaultValue: "Current"),
                    first_sem_start = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    first_sem_end = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    second_sem_start = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    second_sem_end = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_years", x => x.school_year_id);
                    table.CheckConstraint("chk_year_range", "year_end = year_start + 1");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    account_id = table.Column<int>(type: "int", nullable: false),
                    first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    middle_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    avatar_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_users_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "full_amount",
                columns: table => new
                {
                    full_amount_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    school_year_id = table.Column<int>(type: "int", nullable: false),
                    semester = table.Column<string>(type: "longtext", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    semester_status = table.Column<string>(type: "longtext", nullable: false, defaultValue: "Current")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_full_amount", x => x.full_amount_id);
                    table.ForeignKey(
                        name: "FK_full_amount_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "academic_profile",
                columns: table => new
                {
                    academic_profile_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    course_id = table.Column<int>(type: "int", nullable: false),
                    school_year_id = table.Column<int>(type: "int", nullable: true),
                    semester_entered = table.Column<string>(type: "longtext", nullable: true),
                    year_level = table.Column<int>(type: "int", nullable: true),
                    section = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    academic_status = table.Column<string>(type: "longtext", nullable: false, defaultValue: "Enrolled")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academic_profile", x => x.academic_profile_id);
                    table.ForeignKey(
                        name: "FK_academic_profile_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "course_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_academic_profile_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_academic_profile_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_fee_payments",
                columns: table => new
                {
                    payment_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    user_id = table.Column<int>(type: "int", nullable: true),
                    full_amount_id = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    payment_status = table.Column<string>(type: "varchar(10)", nullable: false),
                    received_by = table.Column<int>(type: "int", nullable: false),
                    payment_date = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    year_level_at_payment = table.Column<int>(type: "int", nullable: true),
                    section_at_payment = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_fee_payments", x => x.payment_id);
                    table.ForeignKey(
                        name: "FK_org_fee_payments_accounts_received_by",
                        column: x => x.received_by,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_fee_payments_full_amount_full_amount_id",
                        column: x => x.full_amount_id,
                        principalTable: "full_amount",
                        principalColumn: "full_amount_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_fee_payments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    expense_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    description = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    recorded_by = table.Column<int>(type: "int", nullable: false),
                    expense_date = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    school_year_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.expense_id);
                    table.ForeignKey(
                        name: "FK_expenses_accounts_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "other_funds",
                columns: table => new
                {
                    fund_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    source = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    received_by = table.Column<int>(type: "int", nullable: false),
                    received_date = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    school_year_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_other_funds", x => x.fund_id);
                    table.ForeignKey(
                        name: "FK_other_funds_accounts_received_by",
                        column: x => x.received_by,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_other_funds_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    receipt_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    receipt_number = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    payment_id = table.Column<int>(type: "int", nullable: true),
                    issued_by = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.receipt_id);
                    table.ForeignKey(
                        name: "FK_receipts_accounts_issued_by",
                        column: x => x.issued_by,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_org_fee_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "org_fee_payments",
                        principalColumn: "payment_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "treasurer_signatures",
                columns: table => new
                {
                    signature_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    account_id = table.Column<int>(type: "int", nullable: false),
                    signature_data = table.Column<string>(type: "mediumtext", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)"),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_treasurer_signatures", x => x.signature_id);
                    table.ForeignKey(
                        name: "FK_treasurer_signatures_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_images",
                columns: table => new
                {
                    image_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    expense_id = table.Column<int>(type: "int", nullable: false),
                    image_path = table.Column<string>(type: "varchar(255)", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_images", x => x.image_id);
                    table.ForeignKey(
                        name: "FK_expense_images_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "expense_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    report_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    report_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    school_year_id = table.Column<int>(type: "int", nullable: true),
                    semester = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    date_from = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    date_to = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    beginning_balance = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    total_revenue = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    total_expenses = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    running_balance = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "Draft"),
                    created_by = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP(6)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.report_id);
                    table.ForeignKey(
                        name: "FK_reports_accounts_created_by",
                        column: x => x.created_by,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reports_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "report_items",
                columns: table => new
                {
                    item_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    report_id = table.Column<int>(type: "int", nullable: false),
                    item_type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    item_ref_id = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_items", x => x.item_id);
                    table.ForeignKey(
                        name: "FK_report_items_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "report_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_fee_exemptions",
                columns: table => new
                {
                    exemption_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    school_year_id = table.Column<int>(type: "int", nullable: false),
                    semester = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_fee_exemptions", x => x.exemption_id);
                    table.ForeignKey(
                        name: "FK_student_fee_exemptions_school_years_school_year_id",
                        column: x => x.school_year_id,
                        principalTable: "school_years",
                        principalColumn: "school_year_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_student_fee_exemptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Indexes
            migrationBuilder.CreateIndex("uq_accounts_school_id", "accounts", "school_id", unique: true);
            migrationBuilder.CreateIndex("uq_users_account", "users", "account_id", unique: true);
            migrationBuilder.CreateIndex("uq_course_code", "courses", "course_code", unique: true);
            migrationBuilder.CreateIndex("uq_academic_user", "academic_profile", "user_id", unique: true);
            migrationBuilder.CreateIndex("IX_academic_profile_course_id", "academic_profile", "course_id");
            migrationBuilder.CreateIndex("IX_academic_profile_school_year_id", "academic_profile", "school_year_id");
            migrationBuilder.CreateIndex("uq_receipt_number", "receipts", "receipt_number", unique: true);
            migrationBuilder.CreateIndex("IX_org_fee_payments_full_amount_id", "org_fee_payments", "full_amount_id");
            migrationBuilder.CreateIndex("IX_org_fee_payments_received_by", "org_fee_payments", "received_by");
            migrationBuilder.CreateIndex("IX_org_fee_payments_user_id", "org_fee_payments", "user_id");
            migrationBuilder.CreateIndex("IX_expenses_recorded_by", "expenses", "recorded_by");
            migrationBuilder.CreateIndex("IX_expenses_school_year_id", "expenses", "school_year_id");
            migrationBuilder.CreateIndex("IX_other_funds_received_by", "other_funds", "received_by");
            migrationBuilder.CreateIndex("IX_other_funds_school_year_id", "other_funds", "school_year_id");
            migrationBuilder.CreateIndex("IX_receipts_issued_by", "receipts", "issued_by");
            migrationBuilder.CreateIndex("IX_receipts_payment_id", "receipts", "payment_id");
            migrationBuilder.CreateIndex("IX_treasurer_signatures_account_id", "treasurer_signatures", "account_id");
            migrationBuilder.CreateIndex("IX_expense_images_expense_id", "expense_images", "expense_id");
            migrationBuilder.CreateIndex("IX_reports_created_by", "reports", "created_by");
            migrationBuilder.CreateIndex("IX_reports_school_year_id", "reports", "school_year_id");
            migrationBuilder.CreateIndex("IX_report_items_report_id", "report_items", "report_id");
            migrationBuilder.CreateIndex("IX_student_fee_exemptions_school_year_id", "student_fee_exemptions", "school_year_id");
            migrationBuilder.CreateIndex("IX_student_fee_exemptions_user_id", "student_fee_exemptions", "user_id");
            migrationBuilder.CreateIndex(
                name: "uq_exemption",
                table: "student_fee_exemptions",
                columns: new[] { "user_id", "school_year_id", "semester" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "report_items");
            migrationBuilder.DropTable(name: "expense_images");
            migrationBuilder.DropTable(name: "receipts");
            migrationBuilder.DropTable(name: "treasurer_signatures");
            migrationBuilder.DropTable(name: "student_fee_exemptions");
            migrationBuilder.DropTable(name: "academic_profile");
            migrationBuilder.DropTable(name: "reports");
            migrationBuilder.DropTable(name: "expenses");
            migrationBuilder.DropTable(name: "other_funds");
            migrationBuilder.DropTable(name: "org_fee_payments");
            migrationBuilder.DropTable(name: "courses");
            migrationBuilder.DropTable(name: "full_amount");
            migrationBuilder.DropTable(name: "users");
            migrationBuilder.DropTable(name: "school_years");
            migrationBuilder.DropTable(name: "accounts");
        }
    }
}
