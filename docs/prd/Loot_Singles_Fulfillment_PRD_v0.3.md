# Loot Singles Fulfillment

## Product Requirements Document

**Version:** 0.3\
**Status:** Discovery / Product Definition\
**Working Product Name:** Loot Singles Fulfillment\
**Primary Business:** Loot Card Shop\
**Product Owner:** Loot Card Shop Owner

------------------------------------------------------------------------

# 1. Product Summary

Loot Singles Fulfillment is an internal fulfillment application designed
to help Loot Card Shop employees accurately pick and, in future
versions, pack trading card singles orders.

Version 1 focuses specifically on the **picker experience for TCGplayer
singles orders**.

Today, pickers work from printed TCGplayer invoices. An employee takes
an invoice, finds the requested cards in boxes organized by set, places
the cards into a baggy, and places the baggy with the invoice. A
different employee later packages the order.

Known fulfillment errors include:

-   Wrong card
-   Wrong quantity
-   Missing card
-   Wrong card variant
-   Same card from the wrong set
-   Wrong order/customer during packing

Both picking and packing errors occur. Some errors are not discovered
until the customer receives the shipment and contacts Loot.

V1 will replace the printed invoice during the **picking portion** of
this workflow with a responsive, visual, purpose-built picking
experience.

------------------------------------------------------------------------

# 2. Problem Statement

TCGplayer provides the information necessary to fulfill an order, but
its printed paperwork is not optimized specifically for the physical
task of picking trading card singles.

A picker must interpret relatively dense order information while moving
between paperwork, physical storage boxes, and individual cards.

The application should make the information most likely to prevent an
error exceptionally easy to notice.

## Known Picking Error: Quantity

One of Loot's most common picking mistakes occurs when a customer orders
multiple copies of the same card.

For example:

> Pikachu\
> Quantity: 4

The picker may identify the correct card but overlook the quantity and
pull only one copy.

V1 should therefore treat quantity as a **high-risk attribute**, not
ordinary metadata.

Prefer:

# PULL 4 COPIES

over:

> Qty: 4

Multi-copy lines should require explicit acknowledgement of the
requested quantity.

## Other Known Picking Errors

Other known errors include:

-   Selecting the wrong variant
-   Selecting the same card from the wrong set, although this occurs
    less frequently
-   Missing an item entirely

The application should emphasize the information needed to distinguish
these products.

------------------------------------------------------------------------

# 3. Product Vision

Create a purpose-built singles fulfillment workflow that helps Loot
employees accurately and efficiently move a sold card from physical
inventory into the correct customer shipment.

The longer-term workflow is:

``` text
TCGplayer Orders
       ↓
Order Import
       ↓
Picker Login
       ↓
Order Selection
       ↓
Guided Picking
       ↓
Pick Complete
       ↓
Physical Order Identification       [Future V2]
       ↓
Packing Verification                [Future V2]
       ↓
Shipment
```

V1 ends at **Pick Complete**.

------------------------------------------------------------------------

# 4. Product Boundary

Loot already has processes and equipment for getting cards into
inventory and making them available for sale.

In particular, Loot owns a **Roca machine**, which is used as part of
getting cards into inventory for sale.

That is a separate workflow.

The product boundary is:

> **Inventory processing ends when a card is available for sale. Loot
> Singles Fulfillment begins when a sold card needs to be physically
> retrieved for an order.**

V1 will not replace or integrate with Roca.

------------------------------------------------------------------------

# 5. Product Principles

## 5.1 Optimize for Picking, Not Order Administration

The application must not simply recreate the TCGplayer invoice on a
screen.

It should reorganize and emphasize information specifically for the
physical picking task.

## 5.2 The Application Should Be Easier Than Paper

Reducing errors is important, but accuracy cannot come from introducing
excessive friction.

Most Loot singles orders contain approximately **1 to 5 cards**.

Occasional orders contain approximately **10 to 50 cards**.

The goal is an experience employees prefer using over printed invoices.

## 5.3 High-Risk Information Gets High Visual Priority

Known sources of mistakes should receive greater visual emphasis.

Currently identified high-risk attributes include:

1.  Quantity greater than one
2.  Variant/printing
3.  Set
4.  Card identity

The information hierarchy should reflect actual picking risk rather than
treating every field equally.

## 5.4 One Product Line Is Not Necessarily One Physical Card

For example:

  Product       Quantity
  ----------- ----------
  Pikachu              3
  Charizard            1

This order contains:

-   **2 product lines / picking steps**
-   **4 physical cards**

The application must preserve this distinction.

## 5.5 Follow Loot's Physical Storage Organization

Loot stores singles in boxes organized by set.

Cards within an individual order should therefore be grouped/sorted by
set.

The picker should ideally finish the required cards from one set before
moving to another.

## 5.6 Never Force the Happy Path

If reality does not match the order data, the employee must be able to
accurately represent what happened.

A picker must never need to falsely mark a card as picked simply to
continue.

## 5.7 Picking Must Be Traceable

Each picker uses an individual account.

The system should be able to answer:

> **Who pulled this order?**

This is intended for workflow coordination and fulfillment traceability,
not employee productivity surveillance.

## 5.8 No Image Is Better Than the Wrong Image

Card images are supplemental picking aids.

The authoritative information about what must be pulled comes from the
imported TCGplayer order data.

An incorrect card image could actively cause a picking error and is
therefore **more harmful than displaying no image**.

The application must never knowingly display an uncertain or approximate
image as though it represents the expected card.

------------------------------------------------------------------------

# 6. Current Workflow

The currently understood Loot workflow is:

1.  TCGplayer orders are received.
2.  Invoices are printed.
3.  Printed invoices are placed into a stack.
4.  A picking employee takes an invoice.
5.  Singles are physically stored in boxes organized by set.
6.  The employee uses the invoice's card details to locate each card.
7.  Cards are identified visually.
8.  The requested cards are pulled.
9.  Cards belonging to the order are placed in a baggy.
10. The baggy is placed on top of the corresponding invoice.
11. Completed picked orders are placed into a pile.
12. The pile goes to a different employee.
13. That employee packages the orders.
14. If an incorrect order reaches a customer, the customer contacts
    Loot.

V1 replaces steps 2 through 8 for the picker with a digital picking
workflow.

The packing workflow remains unchanged in V1.

------------------------------------------------------------------------

# 7. Primary V1 User: Picker

A picker is a Loot employee responsible for physically retrieving
singles required for customer orders.

The picker needs to be able to:

-   Log in quickly
-   See orders ready for picking
-   Choose an order
-   Ask the application for the next order
-   Understand which set/storage box to access
-   Identify the required card
-   Clearly see required quantity
-   Clearly see important variant information
-   Confirm successful picking
-   Report picking problems
-   Resume interrupted work
-   Complete an order

Multiple employees may be picking simultaneously.

------------------------------------------------------------------------

# 8. Supported Devices

V1 must provide a responsive experience suitable for:

-   Desktop
-   Mobile phone

The application should not assume that all picking occurs from one
device type.

The interaction may adapt to available screen size while maintaining the
same underlying workflow.

------------------------------------------------------------------------

# 9. Employee Authentication

V1 will use **application-managed individual employee accounts** stored
in Loot's own application database.

Third-party authentication such as Google or Microsoft is not required.

Loot employees do not necessarily have individual company email
accounts, making external identity-provider integration unnecessarily
cumbersome for this workflow.

## 9.1 Authentication Direction

The leading V1 approach is:

**Employee username + PIN**

Each employee should have their own credential.

A shared employee PIN must not be used because it would eliminate the
ability to determine who picked an order.

On a shared device, the experience could allow an employee to select or
enter their username and then enter their PIN.

On an employee's own phone, an appropriate authenticated session may
reduce how frequently the PIN must be re-entered.

## 9.2 Credential Security

PINs must not be stored in plaintext.

Implementation must use appropriate secure credential storage,
failed-attempt protections, session management, and credential reset
mechanisms.

Exact security implementation belongs in technical planning.

## 9.3 Employee Records

The conceptual employee model should support information such as:

-   Employee identifier
-   Display name
-   Authentication credential
-   Active/inactive status
-   Role

## 9.4 Roles

Likely V1 roles are:

**Picker**

Can perform normal picking workflows and report issues.

**Manager/Admin**

May additionally manage employees and perform operational actions
requiring elevated permissions.

Exact Manager/Admin capabilities remain to be finalized.

------------------------------------------------------------------------

# 10. Order Queue

After authentication, employees should be able to view orders available
for picking.

V1 supports two methods of starting work.

## Pick Next Order

The picker can ask the application to select the next available order.

The exact prioritization rules for "next" remain open.

## Choose Order

The picker can freely select a particular available order.

This accommodates situations where employees need to work on a specific
order rather than strictly following system order.

------------------------------------------------------------------------

# 11. Concurrent Picking and Order Claiming

Multiple Loot employees may pick orders simultaneously.

Once an employee begins an order, that order must be **exclusively
claimed**.

Another picker must not be able to simultaneously begin the same order.

Order claiming must be concurrency-safe.

For example, if two employees press **Pick Next Order** at approximately
the same time, the system must assign them different orders.

Other employees should be able to see that an order is already being
worked.

Example:

> **Order #12345**\
> In Progress · Picking by Sam

The exact release/reassignment rules remain open.

------------------------------------------------------------------------

# 12. Guided Picking Experience

After starting an order, the application guides the employee through its
product lines.

The experience should prioritize **one current picking task at a time**
instead of displaying a dense invoice.

A conceptual screen might contain:

> **SURGING SPARKS**
>
> \[Large exact card image, when confidently available\]
>
> **Pikachu ex**\
> 238/191\
> Special Illustration Rare\
> Near Mint
>
> # PULL 3 COPIES
>
> Product 2 of 5

The exact visual design remains a UX decision.

------------------------------------------------------------------------

# 13. Set-Aware Picking

Cards within an order must be grouped/sorted by set.

This aligns the application with Loot's physical storage organization.

For example:

``` text
SURGING SPARKS

Card A
Card B
Card C

↓ SET COMPLETE ↓

DESTINED RIVALS

Card D
Card E
```

The application may provide explicit transitions such as:

> **Surging Sparks complete**\
> Next set: **Destined Rivals**

The goal is to reduce unnecessary movement between storage boxes.

------------------------------------------------------------------------

# 14. Card Information

Each picking step should display the information necessary to identify
the expected physical card.

Where available, this includes:

-   Card image
-   Card name
-   Set
-   Card/collector number
-   Variant/printing
-   Condition
-   Quantity

Additional useful information may include:

-   Rarity
-   Language
-   Game/product line

Visual priority should be based on usefulness during picking rather than
simply displaying every available field.

------------------------------------------------------------------------

# 15. Quantity Requirements

Quantity is a critical V1 requirement.

When quantity is greater than one, it must receive unusually prominent
visual treatment.

For example:

# PULL 4 COPIES

rather than:

> Quantity: 4

The picker should explicitly acknowledge the required quantity before
advancing.

The system must distinguish:

-   Number of unique products
-   Number of physical cards

Progress and completion screens may use both values where helpful.

------------------------------------------------------------------------

# 16. Variant Requirements

Variant differences are a known source of picking mistakes.

Variant/printing information should therefore be visually prominent.

Examples may include:

-   Foil
-   Nonfoil
-   Reverse Holo
-   Holofoil
-   Showcase
-   Surge Foil
-   Other game-specific treatments

The exact presentation may vary by game.

The application must not assume that an image alone communicates every
relevant variant.

Textual variant information should remain visible where necessary.

------------------------------------------------------------------------

# 17. Card Image Accuracy

Card images are intended to help the picker identify the correct
physical card.

They are **not authoritative order data**.

The imported TCGplayer order information remains the source of truth.

## 17.1 Hard Product Rule

> **No image is better than the wrong image.**

An incorrect image could cause the exact picking error the application
is intended to prevent.

Therefore:

-   The application must display an image only when it can confidently
    associate that image with the expected card/printing.
-   The application must not select an image merely because it is the
    closest or most likely match.
-   Same-named cards from another set must not be substituted.
-   Ambiguous catalog results must not silently select a result.
-   If no sufficiently confident match exists, no card image should be
    displayed.
-   Authoritative textual order information must remain available when
    an image is unavailable.
-   The UI should make it clear when an image could not be confidently
    matched.

## 17.2 Required Failure Behavior

Given an imported order line for which catalog enrichment produces:

-   No match
-   Multiple plausible matches
-   An insufficiently confident match

when the picker views the product:

**Then:**

-   No card image is displayed.
-   The original identifying information remains visible.
-   The application does not select a best-guess image.

------------------------------------------------------------------------

# 18. Picking Progress

The picker should always be able to understand their current position
within the order.

Potential information includes:

> Product 3 of 5

and:

> 6 of 9 physical cards accounted for

The picker should be able to navigate backward or review the order
rather than being permanently committed by an accidental swipe/action.

------------------------------------------------------------------------

# 19. Picking Issues

Picking problems are a first-class workflow.

At any product line, the employee must be able to report an issue rather
than falsely confirming a successful pick.

Potential issue categories include:

-   Card not found
-   Insufficient quantity
-   Wrong card in storage location
-   Wrong variant available
-   Wrong condition available
-   Card damaged
-   Inventory discrepancy
-   Card/order information appears incorrect
-   Other

The final issue taxonomy remains to be validated.

## 19.1 Structured Issue Information

Where useful, issues should capture structured information.

For example:

> **Required:** 4\
> **Found:** 2

is preferable to only recording:

> Quantity problem

## 19.2 Notes

Employees may optionally add a note.

Common issue reporting should not require unnecessary typing.

## 19.3 Issue Traceability

The application should retain:

-   Order
-   Product line
-   Issue type
-   Relevant quantity information
-   Optional note
-   Employee who reported the issue
-   Timestamp

------------------------------------------------------------------------

# 20. Order Status

The current conceptual states are:

``` text
Ready
  ↓
In Progress
  ↓
Picked
```

When a problem prevents successful completion:

``` text
Ready
  ↓
In Progress
  ↓
Needs Attention
```

An order with an unresolved picking issue must **not** be represented as
successfully picked.

Additional states or transitions may be needed for:

-   Released orders
-   Abandoned orders
-   Resumed orders
-   Resolved issues
-   Cancelled orders

These remain to be designed.

------------------------------------------------------------------------

# 21. Dashboard

A potential V1 dashboard may provide views for:

**Ready**

Orders available for picking.

**In Progress**

Orders currently claimed by employees.

**Needs Attention**

Orders containing unresolved picking problems.

**Picked**

Orders successfully completed.

Example:

> **Ready --- 14**
>
> Order #1024 · 3 products · 5 cards
>
> **In Progress --- 2**
>
> Order #1021 · Picking by Alex
>
> **Needs Attention --- 1**
>
> Order #1019\
> Pikachu ex\
> Required: 3 · Found: 2

Exact dashboard design remains subject to UX validation.

------------------------------------------------------------------------

# 22. Pick Completion

An order cannot become `Picked` until every required product line has
been successfully acknowledged and no unresolved blocking picking issues
remain.

A completion screen may summarize:

> **Pick Complete**
>
> 5 products\
> 8 physical cards\
> All items picked

This may provide an additional opportunity to catch quantity mistakes.

------------------------------------------------------------------------

# 23. Interruptions and Recovery

Because V1 replaces the printed invoice during picking, losing
application state must not cause the employee to lose their place.

The application should preserve meaningful in-progress state when:

-   The page refreshes
-   The employee accidentally navigates away
-   The employee is interrupted
-   The employee signs out and returns
-   Connectivity is temporarily interrupted

The application must not falsely record a card as picked merely because
the interface advanced.

Full offline operation has not yet been established as a V1 requirement.

------------------------------------------------------------------------

# 24. TCGplayer Integration Discovery

Several real exports from Loot's TCGplayer seller workflow were
investigated during product discovery.

The findings materially affect V1's data strategy.

## 24.1 Order List CSV

The Order List provides order-level information and stable order
identifiers.

It does **not** provide individual card line items.

Therefore it cannot independently drive V1 picking.

## 24.2 Shipping Export CSV

The Shipping Export provides order identifier, customer/shipping
information, item count, shipping information, and other order-level
information.

It does **not** provide individual card line items.

It is not suitable as the primary V1 picking input.

It may become relevant to the future packing workflow.

## 24.3 Pull Sheet CSV

Loot's real Pull Sheet provides useful product-level information
including:

-   Product line/game
-   Product name
-   Condition
-   Card number
-   Set
-   Rarity
-   Quantity
-   Set release date

The export contains a `Main Photo URL` field, but that field was blank
for the products examined.

Quantity greater than one is represented directly.

Real Loot data contained quantities of 2, 3, and 4.

Variant information may appear in product names and/or conditions.

However, the Pull Sheet does **not** associate each product row with an
individual order.

It identifies the orders contained in the overall Pull Sheet but does
not provide:

`Order → individual product lines`

Therefore the Pull Sheet cannot independently support V1's **Pick by
Order** workflow.

It may be particularly valuable for a future **Batch Pick by Set**
workflow.

## 24.4 Packing Slip PDF

The TCGplayer Packing Slip PDF was found to preserve the relationship V1
needs:

> **Order → Individual Product Lines → Quantity**

The inspected packing slips include:

-   TCGplayer order number
-   Individual products belonging to the order
-   Quantity
-   Product description
-   Set
-   Card number
-   Rarity
-   Condition
-   Variant information in relevant descriptions

This makes the Packing Slip PDF the first non-API artifact found that
can reconstruct the V1 order model.

------------------------------------------------------------------------

# 25. Working V1 Order Import Strategy

Unless TCGplayer provides Loot with a better supported integration
mechanism, the current V1 assumption is:

> **TCGplayer Packing Slip PDF import will provide the initial order and
> line-item data.**

Conceptually:

``` text
TCGplayer Packing Slip PDF
          ↓
Defensive Order Parser
          ↓
Order + Line Items
          ↓
Validation
          ↓
Catalog Enrichment
          ↓
Picker Workflow
```

Packing-slip parsing is considered a practical internal-tool
integration, but not an ideal long-term API contract.

TCGplayer may change the document format in the future.

The architecture should therefore isolate TCGplayer document parsing
from the rest of the application.

A future TCGplayer API or supported integration should be able to
replace the PDF importer without redesigning the picker workflow.

------------------------------------------------------------------------

# 26. Import Integrity

Because packing slips are human-readable documents rather than a
structured API contract, V1 must parse them defensively.

The application must favor **rejection over silently importing
questionable order data**.

Potential validation includes:

-   Expected number of orders
-   Number of successfully parsed orders
-   Presence of an order identifier
-   Presence of product lines
-   Valid quantities
-   Required card-identification fields
-   Duplicate order detection

If the importer cannot confidently reconstruct the expected data, it
must surface an import problem rather than silently create an incomplete
or incorrect picking order.

The exact validation rules remain to be specified technically.

------------------------------------------------------------------------

# 27. Customer Privacy

TCGplayer packing slips contain customer information that the picker
does not require, including shipping information.

V1 should follow data minimization principles.

The picker workflow should not expose unnecessary customer information.

Where technically practical, V1 should extract and retain only
information required for picking and order identification rather than
persisting customer shipping PII.

The packing workflow may have different requirements in V2.

------------------------------------------------------------------------

# 28. TCGplayer API

TCGplayer has APIs capable of providing structured store order and
order-line information.

However, TCGplayer's current developer documentation states that new API
access is not currently being granted.

Loot has contacted/is expected to contact TCGplayer to determine
whether:

1.  Existing Pro Sellers can receive access for a private internal
    fulfillment application, or
2.  TCGplayer recommends another supported programmatic integration for
    this use case.

V1 development should not depend on receiving new API access.

If supported API access becomes available, it should be evaluated as a
replacement for packing-slip parsing.

------------------------------------------------------------------------

# 29. Card Catalog Enrichment

The imported TCGplayer order data may not provide everything necessary
for the intended visual picking experience.

In particular, card images require enrichment.

V1 should use a **provider-based catalog architecture** rather than
assuming one universal trading-card catalog.

Conceptually:

``` text
Imported Order Line
        ↓
Identify Game
        ↓
Game-Specific Catalog Provider
        ↓
Candidate Card Record
        ↓
Exact/Confident Match?
      ↙       ↘
    Yes        No
     ↓          ↓
Image +       Text Only
Metadata
```

The picking workflow should not need to know which external catalog
supplied the enrichment.

------------------------------------------------------------------------

# 30. V1 Games

Current V1 target games are:

-   **Pokémon Trading Card Game**
-   **Magic: The Gathering**
-   **Disney Lorcana**
-   **One Piece Card Game**

V1 is not required to support every trading card game available through
TCGplayer.

Additional games can be added deliberately if Loot's fulfillment needs
justify them.

------------------------------------------------------------------------

# 31. Catalog Provider Research

Initial technical discovery has identified promising enrichment sources.

## Magic: The Gathering

**Scryfall** is the leading candidate.

Its data supports printing-level information and card imagery and
appears well suited to matching using set and collector number, followed
by card-name verification.

## Pokémon

**Pokémon TCG API** is a leading candidate.

It provides structured card/set information and image URLs.

## Disney Lorcana

**Lorcast** is a leading candidate.

It provides structured card data, collector numbers, set information,
variants, and imagery.

## One Piece

A suitable catalog/image provider still requires investigation.

Provider licensing, usage terms, availability, and reliability must be
considered before implementation.

These provider selections are not yet permanent architecture
commitments.

------------------------------------------------------------------------

# 32. Card Matching Strategy

Card enrichment must be conservative.

A conceptual matching process is:

1.  Determine game.
2.  Normalize imported set information.
3.  Parse collector/card number.
4.  Parse card name.
5.  Parse relevant variant information.
6.  Query the appropriate game-specific provider.
7.  Prefer exact set + collector-number identity.
8.  Verify the returned card name.
9.  Validate relevant printing/variant information where possible.
10. Accept the enrichment only when sufficiently confident.
11. Otherwise provide text-only picking.

Name-only fuzzy matching must not be sufficient to automatically display
an image.

Exact matching rules may differ by game.

------------------------------------------------------------------------

# 33. Source of Truth

The system must distinguish between:

**Authoritative order information**

Data imported from the TCGplayer fulfillment artifact describing what
Loot sold and must pull.

and:

**Supplemental catalog enrichment**

External metadata intended to improve the picking experience.

Catalog enrichment must not silently overwrite authoritative order
attributes.

If enrichment disagrees with the imported order, the application should
not simply assume the enrichment provider is correct.

------------------------------------------------------------------------

# 34. Auditability

V1 should retain enough history to answer:

> Who picked this order?

Relevant information may include:

-   Order identifier
-   Picker
-   Pick started
-   Pick completed
-   Picking issues
-   Issue reporter
-   Relevant timestamps

Detailed employee productivity analytics, rankings, or leaderboards are
not currently product goals.

------------------------------------------------------------------------

# 35. V1 Non-Goals

V1 will not attempt to:

-   Replace TCGplayer
-   Manage sellable card inventory
-   Add cards to inventory
-   Replace Roca
-   Integrate with Roca
-   Price cards
-   List cards for sale
-   Process customer payments
-   Purchase shipping labels
-   Replace Loot's shipping system
-   Perform packing verification
-   Generate physical order barcodes for packing
-   Automatically identify arbitrary physical cards with a camera
-   Require AI or computer vision
-   Automatically correct TCGplayer inventory discrepancies
-   Support every TCG sold on TCGplayer
-   Become a commercial multi-store SaaS platform

------------------------------------------------------------------------

# 36. Future V2: Packing

A future version may address packing and order-association errors.

A possible workflow is:

``` text
Pick Complete
      ↓
Generate Order QR / Barcode
      ↓
Attach Identifier to Bag
      ↓
Packer Login
      ↓
Scan Bag
      ↓
Display Expected Order
      ↓
Packing Verification
      ↓
Pack Complete
```

This could establish:

> **Picked by:** Employee A\
> **Packed by:** Employee B

and help prevent wrong-order/customer errors.

V2 is explicitly outside V1 scope.

------------------------------------------------------------------------

# 37. Future: Batch Picking by Set

A future workflow may allow employees to pick across multiple orders
according to physical storage location.

For example:

> **SURGING SPARKS**
>
> Pikachu ex · 238/191
>
> # PULL 5 TOTAL
>
> Order 123: 3\
> Order 456: 1\
> Order 789: 1

The picker could work through each set once and later separate the cards
into customer orders.

The TCGplayer Pull Sheet CSV discovered during V1 research may be
particularly suitable for this workflow because it aggregates the
products required across a selected batch of orders.

V1 does not implement batch picking.

However, the underlying domain model should avoid unnecessarily assuming
that a picking session can only ever span one complete order.

------------------------------------------------------------------------

# 38. Future: Assisted Card Verification

If real V1 usage demonstrates that incorrect-card picks remain a
significant problem, a future version may investigate:

-   Camera-assisted expected-card comparison
-   Computer vision
-   Card recognition

This should not be implemented merely because the technology is
available.

It should address a demonstrated remaining fulfillment problem.

------------------------------------------------------------------------

# 39. Success Criteria

The primary product outcome is:

> **Reduce customer-facing TCGplayer singles fulfillment errors
> originating during picking.**

Potential measurements include:

-   Customer-reported singles picking errors
-   Quantity-related errors
-   Wrong-card errors
-   Wrong-variant errors
-   Picking issues caught before shipment
-   Orders processed through the application
-   Picking error rate

The second major outcome is usability:

> **The digital workflow must be sufficiently fast and convenient that
> Loot employees prefer it to picking from printed invoices.**

Potential measurements include:

-   Average picking time
-   Employee feedback
-   Adoption rate
-   Percentage of eligible orders processed digitally
-   Abandonment/reversion to paper

Baseline measurements still need to be established.

------------------------------------------------------------------------

# 40. Technology and Architecture Decisions

The following technology choices are approved for V1 and should be
treated as the default implementation direction unless later technical
discovery identifies a concrete reason to change them.

## 40.1 Application Model

V1 will be delivered as a **responsive Progressive Web App (PWA)**.

A single web application will support both desktop and mobile-phone use
rather than maintaining separate native desktop, iOS, or Android
applications.

The experience should adapt to the device:

-   Desktop should favor operational views such as order queues,
    imports, issue review, and employee administration.
-   Mobile should favor the focused picking workflow, with large card
    imagery when safely available, prominent quantity and variant
    information, and touch-friendly controls.
-   The application may be installable to a supported device's home
    screen through normal PWA capabilities.

Native mobile applications and Electron-style desktop applications are
not required for V1.

## 40.2 Frontend

The V1 frontend will use:

-   **React**
-   **TypeScript**
-   Responsive web design
-   Progressive Web App capabilities

The frontend should provide one coherent application with responsive
layouts rather than separate desktop and mobile codebases.

The exact component library and styling approach remain technical
implementation decisions.

## 40.3 Backend

The V1 backend will use:

-   **ASP.NET Core Web API**
-   **C#**

The backend is responsible for authoritative business rules including:

-   Authentication and authorization
-   Order claiming and concurrency
-   Order import orchestration
-   Picking state
-   Picking issues
-   Auditability
-   Catalog enrichment coordination
-   Persistence

Critical workflow rules, especially exclusive order claiming, must be
enforced server-side rather than relying only on frontend behavior.

## 40.4 Data Access and Database

V1 will use:

-   **Entity Framework Core**
-   **Azure SQL Database**

The exact database schema will be defined during specification and
technical planning rather than being fixed by this PRD.

Likely domain concepts include employees, orders, order lines, picking
activity, picking issues, imports, and catalog matches.

## 40.5 Authentication Technology

Employee authentication will be implemented using established ASP.NET
Core authentication and credential-security primitives while preserving
the Product Owner-approved **individual username + PIN** experience.

PINs must be securely hashed and must never be stored in plaintext.

For the internal browser application, secure cookie-based authentication
is the preferred V1 direction unless technical planning identifies a
concrete requirement for a different mechanism.

V1 does not require Google, Microsoft, or another third-party identity
provider.

## 40.6 Order Import Architecture

TCGplayer order ingestion must be isolated behind an import boundary so
the rest of the application does not depend directly on packing-slip PDF
structure.

The working V1 implementation is:

``` text
TCGplayer Packing Slip PDF
          ↓
TCGplayer Packing Slip Importer
          ↓
Defensive Parsing + Validation
          ↓
Normalized Orders + Order Lines
          ↓
Application Workflow
```

The design should support replacing the packing-slip importer with a
future supported TCGplayer API or other integration without redesigning
the picking domain or user experience.

PDF parsing must remain defensive and follow the import-integrity
requirements defined elsewhere in this PRD.

## 40.7 Card Catalog Provider Architecture

Catalog enrichment must use a provider abstraction so game-specific
external services are isolated from the picking workflow.

The current provider direction is:

-   **Magic: The Gathering:** Scryfall
-   **Pokémon Trading Card Game:** Pokémon TCG API
-   **Disney Lorcana:** Lorcast
-   **One Piece Card Game:** Provider still to be selected

The application should consume normalized card-display/enrichment data
rather than embedding provider-specific behavior throughout the UI or
domain.

Provider replacement must not require redesigning the picker workflow.

All enrichment continues to follow the hard product rule:

> **No image is better than the wrong image.**

## 40.8 Hosting

The approved V1 Azure hosting direction is:

``` text
React + TypeScript PWA
          ↓
Azure Static Web Apps

ASP.NET Core Web API
          ↓
Azure Container Apps

Entity Framework Core
          ↓
Azure SQL Database
```

The application is intended for Loot Card Shop's internal use and does
not need multi-tenant SaaS architecture.

Infrastructure should prioritize:

-   Security
-   Reliability during fulfillment work
-   Low and predictable operating cost
-   Simple maintenance
-   Isolation of Loot's application data

The project should optimize for low cost, but **zero-dollar hosting is
not a requirement if achieving it would introduce unacceptable
production downtime or reliability problems**.

## 40.9 Testing Direction

The planned testing stack is:

-   **xUnit** for backend tests
-   **Vitest** and **React Testing Library** for frontend unit/component
    tests
-   **Playwright** for critical end-to-end workflows

Critical automated coverage should include high-risk behaviors such as:

-   Two employees cannot claim the same order
-   Quantity greater than one is preserved through import and picking
-   An unresolved picking issue prevents successful completion
-   Ambiguous catalog enrichment does not display a guessed card image
-   Packing-slip parsing fails safely when required order data cannot be
    reconstructed
-   Interrupted picking work can be resumed without falsely recording
    progress

## 40.10 Architectural Principles

V1 implementation should preserve the following boundaries:

1.  **TCGplayer import is replaceable.** Packing-slip parsing is an
    adapter, not the application domain.
2.  **Catalog providers are replaceable.** External card APIs must not
    become the source of truth for sold order data.
3.  **The backend owns critical workflow rules.** The frontend must not
    be the only enforcement point for concurrency, authentication, or
    order state.
4.  **Desktop and mobile share one product.** Responsive UX differences
    should not create separate fulfillment systems.
5.  **V1 is single-business software.** Do not introduce multi-tenant
    complexity for hypothetical future customers.
6.  **Reliability matters during fulfillment.** Cost optimization must
    not knowingly make the production picking workflow unreliable.

------------------------------------------------------------------------

# 41. Remaining Open Questions

These questions should remain visible until resolved. They must not be
silently converted into implementation assumptions.

## Order Queue and Claiming

1.  How should **Pick Next Order** determine which order comes next?
2.  Should shipping deadlines affect priority?
3.  How should a picker release an order they started?
4.  When released, should completed progress remain?
5.  How should abandoned orders be detected?
6.  How long can an inactive order remain claimed?
7.  Can managers force-release or reassign orders?

## Picking Issues

8.  What exact issue categories occur frequently enough to warrant
    structured options?
9.  Who is responsible for resolving `Needs Attention` orders?
10. Can the original picker resolve an issue?
11. When an issue is resolved, where does the order re-enter the
    workflow?
12. Should certain resolutions require manager involvement?
13. What should happen operationally when only some of the requested
    quantity exists?
14. What should happen when the expected card exists only in a different
    condition or variant?
15. Should inventory discrepancies trigger any action outside this
    application?

## Import Workflow

16. Exactly how should employees generate and upload packing slips
    during normal operations?
17. Should an import be all-or-nothing if one order cannot be parsed?
18. How should partially valid imports be handled?
19. How should duplicate packing-slip imports be detected?
20. What happens when an already-imported TCGplayer order changes?
21. How are cancelled orders handled?
22. How should parsing failures be presented to the employee?
23. Should imported source documents be retained after successful
    extraction, or discarded?

## TCGplayer Integration

24. Will TCGplayer grant Loot API access for its internal Pro Seller use
    case?
25. If not, will TCGplayer recommend another supported integration
    mechanism?
26. If API access becomes available later, what migration requirements
    exist for previously imported orders?

## Catalog Enrichment

27. What One Piece catalog/image provider should V1 use?
28. What exact matching rules constitute a sufficiently confident match
    for each supported game?
29. How should set-name differences between TCGplayer and catalog
    providers be normalized?
30. How should variants be normalized across providers?
31. How should languages be handled?
32. What provider usage/licensing restrictions need to be incorporated
    into deployment?
33. Should enriched catalog information be cached locally?
34. What happens if an external catalog provider is temporarily
    unavailable?

## Image Failure

35. Is text-only picking acceptable whenever an exact image cannot be
    obtained?

The current product direction assumes **yes**, because displaying no
image is safer than displaying an incorrect image, but this should be
explicitly confirmed with the Product Owner.

## Authentication

36. What PIN length/complexity is appropriate?
37. Should shared devices display employee names for quick selection or
    require username entry?
38. How long should authenticated sessions remain active?
39. How does an employee reset a forgotten PIN?
40. Which users can create, disable, and modify employee accounts?
41. What exact Manager/Admin permissions are required?

## Picker UX

42. Should primary navigation use swipe, buttons, or both?
43. What interaction should explicitly confirm a quantity greater than
    one?
44. How should desktop behavior differ from mobile?
45. How large should the card image be relative to textual
    identification?
46. Which variant fields need the greatest visual prominence?
47. Should a picker be able to switch between the focused card view and
    a complete order overview?
48. What should appear on the final Pick Complete screen?
49. How should set transitions be presented?

## Reliability

50. Is reliable resume-after-reconnect sufficient for V1, or does Loot
    require offline picking?
51. What should happen to an active session if connectivity disappears
    for an extended period?
52. How should the application protect against accidentally recording a
    pick due to duplicate taps/swipes or navigation?

## Measurement

53. Approximately how many TCGplayer singles orders does Loot fulfill
    per day/week?
54. What is the current customer-reported picking-error rate?
55. What percentage of known errors are quantity-related?
56. Can Loot establish a baseline before V1 rollout?
57. How much additional picking time, if any, is acceptable?
58. What employee feedback would demonstrate that the application is
    preferable to paper?

------------------------------------------------------------------------

# 42. Remaining Discovery Priorities

The highest-priority remaining investigations are:

## Priority 1 --- One Piece Enrichment

Identify and validate a reliable One Piece card catalog/image source
suitable for Loot's internal application.

## Priority 2 --- Issue Resolution Workflow

Determine what actually happens operationally after a picker reports an
issue.

## Priority 3 --- Import UX

Design how employees move from TCGplayer packing-slip generation to
orders appearing in the picker application with minimal friction.

## Priority 4 --- Picker Prototype

Create a lightweight responsive prototype to validate:

-   Card information hierarchy
-   Quantity treatment
-   Set transitions
-   Mobile versus desktop experience
-   Swipe/buttons
-   Order overview
-   Picking issue interaction

This should be validated with people who actually pull Loot orders
before implementation details become fixed.

## Priority 5 --- Baseline Measurement

Establish enough current-state data to determine whether V1 actually
improves fulfillment accuracy and whether it materially affects picking
speed.

------------------------------------------------------------------------

# 43. Product Decision Gate

V1 should move from discovery into implementation planning when there is
reasonable confidence that:

1.  Packing-slip order data can be parsed reliably enough for internal
    use.
2.  Import failures can be detected rather than silently producing
    incorrect orders.
3.  The four target games have an acceptable picking experience,
    including safe behavior when images cannot be obtained.
4.  The One Piece enrichment strategy is understood.
5.  Order claiming supports multiple simultaneous pickers.
6.  The `Needs Attention` workflow has an operational resolution.
7.  Employee authentication requirements are sufficiently defined.
8.  A responsive prototype demonstrates that the experience can
    realistically replace the printed invoice.
9.  Loot employees find the proposed workflow acceptable.
10. Appropriate baseline measurements can be established.

------------------------------------------------------------------------

# 44. Current V1 Product Hypothesis

> **A responsive, visual, set-aware picking application that replaces
> TCGplayer's printed invoice, strongly emphasizes high-risk information
> such as quantity and card variant, safely supplements orders with
> exact card imagery when available, and associates each order with an
> authenticated picker can reduce Loot Card Shop's TCGplayer singles
> picking errors without making fulfillment unacceptably slower.**

This remains a hypothesis until validated through actual use.

------------------------------------------------------------------------

## Current V1 in One Sentence

**Import TCGplayer orders → employee logs in → chooses or receives an
order → app walks them through the cards by set with quantity and
variant impossible to overlook → employee reports anything that doesn't
match reality → successful order is recorded as picked by that
employee.**
