# Feature Specification: Automated Stage and Production Deployment

**Feature Branch**: `019-automated-deployment`

**Created**: 2026-09-22

**Status**: Draft

**Input**: User description: "I want to work on deploying this application so that I can use it at other locations (such as the store). I think it would be good to have two environments, a stage and a prod, with a prod deployment being more purposeful and a stage deployment happening on push to main branch. it should be completely automated, so deploy on merge for stage, a simple button push for deploy to prod. help me plan this out keeping in mind the current decisions we've already made such as deploying on azure and making sure we stick to the no costs requirements."

## Clarifications

### Session 2026-09-22

- Q: For a production release, may the person who started it also approve it? → A: **Yes,
  self-approval is allowed.** The approval gate is a deliberate-pause and named-version control, not
  a two-person control. With two staff, requiring a second approver would make production
  unchangeable — including unfixable — whenever one of them is unavailable, which costs more
  reliability than it buys oversight (constitution Principle XI). The gate still prevents the
  failure it exists for: production cannot change by merging, the version must be named, and the
  release stops and waits for a decision.
- Q: May real customer data — imported packing slips carrying customer names and addresses — exist
  in the stage environment? → A: **Yes, with production-grade protections.** Stage is therefore
  **not** a lower-security environment. Every privacy and security control that applies to
  production applies equally to stage: default-deny database access, the split between an
  application identity and a schema-changing identity, hashed PINs, and logged packing-slip access.
  Stage differs from production in reliability and cost only, never in how customer data is
  protected.
- Q: Can production be released while pickers are actively working? → A: **Yes, at any time.**
  Claims and recorded outcomes are persisted server-side, so a picker whose request is interrupted
  retries and loses nothing. No release window is imposed: the times a fix is most needed are during
  a shift, and a rule forbidding that would be unenforceable and harmful.
- Q: What recovery must production data have if the database is lost or corrupted? → A: **The
  platform's included point-in-time restore, at least 7 days.** Imported order data can be
  re-imported because TCGplayer remains authoritative; what this protects is pick history and stored
  packing slips. Longer retention is a paid add-on and would need a separate cost decision.
- Q: Should the one-time Azure setup become a committed provisioning script? → A: **No — keep it a
  checklist, with instructions written for someone who has never done it.** Correcting the record:
  an earlier draft of this specification justified the manual setup by claiming the repository
  forbids provisioning from code. **It does not.** Nothing in the constitution, the PRD or
  `CLAUDE.md` says anything of the kind. The decision stands on its own merits — the setup runs
  twice, ever, and a script written against a subscription nobody can test first is harder to trust
  than commands a person can read — but it is a **preference, not a prohibition**, and a later
  feature may revisit it freely.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick Orders at the Shop (Priority: P1)

A picker at Loot Card Shop opens the application on a store device, signs in, claims an order and
picks it. Nobody runs anything on a laptop for this to work, and the application is still there the
next morning.

**Why this priority**: This is the entire point of the feature. Today the application exists only on
a developer machine, which means it cannot be used for real fulfillment work at all. Every other
story in this feature is about keeping this one working safely.

**Independent Test**: Deploy the application by hand once, then have someone sign in from a store
device over the public address and complete a pick. Delivers the whole product to the shop even with
no automation in place.

**Acceptance Scenarios**:

1. **Given** the application is deployed and no developer machine is running, **When** a picker opens
   the production address on a store device, **Then** the sign-in screen loads over a secure
   connection with a valid certificate.
2. **Given** a picker is signed in, **When** they claim an order and record a pick, **Then** the
   result is saved and visible to a second picker on a different device.
3. **Given** nobody has used the application for several hours, **When** a picker opens it, **Then**
   it becomes usable without anyone intervening.

---

### User Story 2 - A Merge Reaches Stage by Itself (Priority: P2)

When a pull request merges, that change appears on stage without anyone
typing a command, so it can be exercised against real infrastructure before it reaches the shop.

**Why this priority**: The automation is what makes releasing safe and repeatable, but the shop can
be served by a hand-deployed application first. This story removes the manual step and the mistakes
that come with it.

**Independent Test**: Merge a pull request that makes a visible change, then confirm the change is
live on stage without anyone taking an action after the merge.

**Acceptance Scenarios**:

1. **Given** a pull request is merged, **When** no further human action is taken, **Then** that
   commit is running on stage.
2. **Given** a merge introduces a change that breaks the automated quality checks, **When** the
   deployment runs, **Then** stage is not updated and the failure is reported.
3. **Given** a deployment updates stage and the deployed application then fails its
   post-deployment checks, **When** the failure is detected, **Then** the previously working version
   is restored without anyone intervening.
4. **Given** two merges land close together, **When** both deployments run, **Then** stage ends up
   running the newer of the two, and never an older version.

---

### User Story 3 - A Production Release Is Deliberate and Approved (Priority: P2)

Someone starts a production release, names the exact version to release, and a reviewer approves it
before anything in production changes. What the reviewer approved is what ships.

**Why this priority**: Production serves real fulfillment work and holds customer data. An accidental
or ambiguous release is the failure this story exists to prevent. It ranks with Story 2 because both
become necessary the moment automation exists at all.

**Independent Test**: Start a production release naming a specific version, confirm nothing changes
while it waits, approve it, and confirm production ends up running exactly that version.

**Acceptance Scenarios**:

1. **Given** no release has been started, **When** a pull request merges to the main branch, **Then**
   production is unchanged.
2. **Given** a production release has been started but not yet approved, **When** a reviewer looks at
   the pending request, **Then** they can see which version is being released before deciding.
3. **Given** a production release is waiting for approval, **When** a different change is merged and
   reaches stage, **Then** the waiting release still deploys the version it named.
4. **Given** a production release is waiting for approval, **When** approval is declined or never
   given, **Then** production is never modified.
5. **Given** a production release is approved, **When** it completes, **Then** production runs the
   artifact already built for that version, with nothing rebuilt in between.

---

### User Story 4 - A Picker Stays Signed In (Priority: P3)

A picker signed in earlier in the shift is still signed in after a quiet period, and after a release
goes out mid-day.

**Why this priority**: Being signed out mid-pick is disruptive and erodes trust in the application,
but it does not lose data or block the work outright. It matters enough to be a requirement and not
enough to precede the stories above.

**Independent Test**: Sign in, leave the application untouched long enough for it to go idle, return
and confirm the session survived. Repeat across a release.

**Acceptance Scenarios**:

1. **Given** a picker is signed in and the application then goes idle and restarts, **When** the
   picker returns within the normal session lifetime, **Then** they are still signed in.
2. **Given** a picker is signed in, **When** a new version is released, **Then** they are still
   signed in afterwards.

---

### User Story 5 - Investigate Something the Shop Reported (Priority: P3)

The shop reports that something went wrong earlier. Whoever looks into it can read what the
application recorded at that time, in either environment.

**Why this priority**: This feature deliberately ships without automated alerting, which makes a
report from the shop the way problems surface. That is only workable if the evidence outlives the
report.

**Independent Test**: Cause a recognisable failure, wait until well after it has scrolled out of any
live view, then find the corresponding record.

**Acceptance Scenarios**:

1. **Given** the application recorded a failure some days ago, **When** someone searches the retained
   records for that environment, **Then** they find it.
2. **Given** records are being retained, **When** someone reads them, **Then** they contain no
   customer personal information, PINs, tokens, or connection strings.

---

### Edge Cases

- **A deployment succeeds but the application cannot reach its database.** The deployment must not be
  reported as successful (FR-021).
- **The database is unreachable while the application is running.** The container must not be
  restarted or replaced for that reason alone, because restarting does not fix a database problem
  and removes a working application (FR-020).
- **Two deployments to the same environment overlap.** They must not interleave such that a failing
  deployment's rollback replaces a newer successful one (FR-009).
- **A schema change fails partway.** The environment must not be left serving a version whose schema
  expectations are unmet.
- **A release is approved days after it was started.** It must still deploy the version it named, not
  whatever has become current since.
- **A picker's device sleeps mid-pick and wakes after the application has gone idle.** The claim and
  any recorded outcomes must survive.
- **The address is opened over an insecure connection.** It must be upgraded or refused, never served
  in a way that would transmit the session cookie in the clear.
- **A release goes out while a picker is mid-order.** The claim and every already-recorded outcome
  must survive; the picker may see one failed request and must be able to continue by retrying
  (FR-030).
- **Someone treats stage as disposable because it is "only stage".** Stage may hold real customer
  data, so every control that protects production protects stage too (FR-029). Relaxing a control
  there is the same failure as relaxing it in production.

## Requirements *(mandatory)*

### Functional Requirements

#### Hosting and access

- **FR-001**: The application MUST be reachable at a stable public address over a secure connection
  with a valid certificate, with no developer machine involved.
- **FR-002**: The API and the web interface MUST be served from a single origin. The session cookie
  is restricted to same-site use and cannot be carried across origins.
- **FR-003**: Production MUST be reachable at a subdomain of the shop's existing domain. The
  storefront's own records MUST NOT be affected.
- **FR-004**: An unmatched API route MUST return a not-found response, never the web interface's
  page content.

#### Environments

- **FR-005**: There MUST be exactly two environments, **stage** and **production**, each with its
  own database, its own credentials, and its own retained records.
- **FR-006**: Neither environment MUST be able to read or modify the other's data, and credentials
  issued for deploying to one MUST NOT grant access to the other.
- **FR-007**: Neither environment MUST use the developer's local database.
- **FR-029**: Stage MAY hold real customer data, and therefore MUST carry **every** privacy and
  security control that applies to production — FR-020, FR-021 and FR-022 without exception, plus
  the hashed-PIN and logged packing-slip-access rules already in force. Stage MUST differ from
  production in reliability and cost only, never in how customer data is protected. No control may
  be relaxed on the grounds that an environment is "only stage".

#### Releasing to stage

- **FR-008**: Merging to the main branch MUST deploy that commit to stage with no
  further human action.
- **FR-009**: Deployments to a single environment MUST NOT overlap. A failing deployment's recovery
  MUST NOT replace a newer successful deployment.
- **FR-010**: The automated quality checks MUST pass against the merged result before the test
  environment is updated.

#### Releasing to production

- **FR-011**: Production MUST change only when a person explicitly starts a release and names the
  exact version to be released. Merging code MUST NOT change production.
- **FR-012**: A production release MUST stop and require an explicit approval before any production
  resource is modified. The approver MAY be the same person who started the release (Clarifications,
  2026-09-22); the requirement is that the release pauses for a deliberate decision, not that a
  second person makes it.
- **FR-013**: The version being released MUST be identifiable by the approver at the moment of
  approval, and MUST NOT change between approval and deployment.
- **FR-014**: Production MUST run the artifact already built for the named version. Nothing MUST be
  rebuilt between stage and production.

#### Releasing, both environments

- **FR-015**: Pending schema changes MUST be applied before the new version begins serving requests.
- **FR-016**: Schema changes MUST be backward-compatible with the immediately preceding version, so
  that restoring that version leaves a working application.
- **FR-017**: If a newly deployed version fails its post-deployment checks, the previously running
  version MUST be restored without human action.
- **FR-018**: Post-deployment checks MUST confirm that the web interface is served, that an unmatched
  API route returns not-found, and that an unauthenticated API request is rejected.
- **FR-030**: A release MUST be permitted at any time, including while pickers are working. No
  release window MUST be imposed. A request interrupted by a release MUST NOT lose work that was
  already recorded, and the picker MUST be able to continue by retrying.

#### Security and privacy

- **FR-019**: No secret, credential, connection string, key or PIN MUST be committed to the
  repository, in any environment's configuration or in any workflow definition.
- **FR-020**: The database MUST refuse connections from any source other than its own environment's
  application network. Broad allowances, including any rule permitting all services or all addresses,
  MUST NOT exist.
- **FR-021**: The running application MUST NOT hold permission to modify database schema. Only the
  migration step MUST hold that permission, and neither MUST hold database ownership.
- **FR-022**: Customer personal information, PINs, tokens, secrets and connection strings MUST NOT
  appear in retained records or in any deployment output.

#### Health and verification

- **FR-023**: The check used to decide whether the application process is running MUST NOT depend on
  the database, so that a database problem does not cause a running application to be replaced.
- **FR-024**: A production release MUST verify that the deployed application itself can reach its
  database before the release is reported as successful. A release that cannot MUST fail.
- **FR-025**: Any endpoint added for verification MUST be safe to expose without authentication and
  MUST NOT reveal connection details, credentials or internal error text to its caller.

#### Sessions

- **FR-026**: A signed-in employee MUST remain signed in when the application goes idle and restarts,
  and across a release, for the normal lifetime of their session.

#### Records and cost

- **FR-027**: Records emitted by the application MUST be retained and searchable for at least 30 days
  in both environments.
- **FR-028**: Recurring infrastructure cost MUST NOT exceed $10 per month, and a cost alert MUST be
  in place to report if it does.
- **FR-031**: The production database MUST support restoring to a point in time at least 7 days in
  the past, at no additional cost. This MUST be verified when the database is created, not assumed.
- **FR-032**: A newly created environment MUST provide a way to create its first manager account
  without an existing signed-in user, and that way MUST NOT leave a credential behind once used.

### Key Entities

- **Environment**: A named, isolated place the application runs — stage or production.
  Owns its own database, its own identities, its own retained records and its own address.
- **Release**: A specific version of the application, built once and identified by the commit it was
  built from. The same release may run in stage and later in production.
- **Session key material**: Data the application stores so that a signed-in session survives the
  application restarting. Shared by every instance of one environment, never across environments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A picker at the shop can sign in, claim an order and record a pick on a store device,
  with no developer machine running and no manual startup step.
- **SC-002**: A merged pull request reaches stage with **zero** manual actions after
  the merge.
- **SC-003**: Production cannot be changed without a person naming a version and a reviewer
  approving; attempting to reach production by merging alone leaves it unchanged.
- **SC-004**: The version a reviewer approves is the version running in production afterwards, in
  100% of releases, including releases approved after other changes have been merged.
- **SC-005**: A picker signed in before a release is still signed in after it, and after the
  application has been idle long enough to shut down.
- **SC-006**: A failure the shop reports up to 30 days later can still be found in retained records
  for that environment.
- **SC-007**: A release that leaves the application unable to reach its database is reported as
  failed, not successful.
- **SC-008**: Recurring infrastructure cost stays at or below $10 per month, and an alert exists that
  reports if it does not.
- **SC-009**: A reviewer inspecting the repository finds no committed secret, credential, connection
  string or PIN.
- **SC-010**: Comparing stage's and production's privacy and security controls side by side finds no
  difference: both deny database access by default, both separate the application identity from the
  schema-changing identity, both hash PINs, both log packing-slip access.
- **SC-011**: A release issued while a picker is mid-order leaves that picker's claim and every
  already-recorded outcome intact, and the picker can continue by retrying.
- **SC-012**: Production data can be restored to a chosen point in time at least 7 days earlier, and
  this has been confirmed against the created database rather than assumed.

## Assumptions

- **The hosting platform is Azure**, using container hosting and a managed SQL database, per approved
  PRD §40.8. Cost figures assume production's database is the only paid resource.
- **The source repository is public GitHub** and stays public; the built container image is therefore
  public and must contain no secrets.
- **The shop's domain is managed at Namecheap** and its DNS can be edited without affecting the
  storefront. The custom address may be configured after the first successful release.
- **30 days' record retention** is the assumed minimum because it matches what the platform includes
  at no cost. A longer period would be a cost decision.
- **Stage may be slow or unavailable at times**, because it runs on free-tier
  resources that pause when unused. This is acceptable; production does not share that behaviour.
  Reliability is the only axis on which stage is permitted to be weaker than production — never
  privacy or security (FR-029).
- **Restoring the database is a manual operation.** This feature requires that restore be *possible*
  within 7 days (FR-031); it does not automate restoring, rehearse it, or monitor backup health.
- **Database wake-up behaviour is out of scope.** If a paused database makes a request fail, the
  picker sees an error. Handling that gracefully requires changing order-claiming code and is a
  separate feature.
- **No alerting or uptime monitoring is in scope.** A report from the shop is how problems surface;
  this feature only ensures the evidence is still there when someone looks.
- **Existing behaviour is unchanged**: sign-in, order claiming, picking, packing and importing all
  behave exactly as they do today. This feature adds no product functionality.
- **One instance of the application runs per environment at a time.** Horizontal scaling is out of
  scope.

## Out of Scope

- Retry or resilience handling for a paused database waking up (a separate feature)
- Alerting, paging or uptime monitoring of any kind
- Installable/offline behaviour for the web interface
- Infrastructure defined as code. One-time resource creation is done by hand from a checklist — a
  Product Owner decision of 2026-09-22, not a rule imposed by this repository. See Clarifications.
- Running more than one instance of the application per environment
- Any paid logging, monitoring or application-performance service
- Multi-location or multi-business support; this remains single-business software
