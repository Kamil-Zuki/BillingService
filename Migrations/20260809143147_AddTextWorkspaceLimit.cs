using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingService.Migrations
{
    /// <inheritdoc />
    public partial class AddTextWorkspaceLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "billing",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "Entitlements",
                value: new Dictionary<string, string> { ["maxProjects"] = "3", ["maxCards"] = "500", ["aiRequestsPerDay"] = "10", ["textWorkspaceMaxBooks"] = "3" });

            migrationBuilder.UpdateData(
                schema: "billing",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "Entitlements",
                value: new Dictionary<string, string> { ["maxProjects"] = "50", ["maxCards"] = "10000", ["aiRequestsPerDay"] = "100", ["textWorkspaceMaxBooks"] = "-1" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "billing",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "Entitlements",
                value: new Dictionary<string, string> { ["maxProjects"] = "3", ["maxCards"] = "500", ["aiRequestsPerDay"] = "10" });

            migrationBuilder.UpdateData(
                schema: "billing",
                table: "Plans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "Entitlements",
                value: new Dictionary<string, string> { ["maxProjects"] = "50", ["maxCards"] = "10000", ["aiRequestsPerDay"] = "100" });
        }
    }
}
