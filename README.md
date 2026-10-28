# Aries Magic Appointment System

ASP.NET Core 10 MVC, Entity Framework Core and SQL Server.

The running **closed-alpha application** is hosted on **myASP.NET Windows IIS**:

**https://ariesmagicartist-001-site1.htempurl.com/**

GitHub stores source and runs verification. **GitHub Pages is not the ASP.NET Core application, database, login, booking or payment deployment.** Any Pages content is project documentation only. Alpha availability is not approval for a public customer launch.

- [CI and controlled release instructions](docs/CI_CD.md)
- [Repository publication safeguards](docs/REPOSITORY_SECURITY.md)
- [Final GitHub Actions setup and first-release runbook](docs/GITHUB_ACTIONS_SETUP.md)

The deployment workflow is manual-only and defaults to an inventory comparison without a hosting connection. It must not be enabled until the Environment approval, existing credentials, live configuration, baseline and recovery evidence in the runbook are verified. SQL migrations, seeding and H4 PFX activation are not deployment actions.
