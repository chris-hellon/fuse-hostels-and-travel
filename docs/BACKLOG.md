# Backlog

Planned work that isn't started yet.

## Hostel Bookings page (admin): first version built, awaiting review

The admin's Bookings section splits into **Tour Bookings** (today's bookings pages) and **Hostel Bookings**.

Hostel Bookings is view only: no bookings are made, changed or cancelled here.

- Fetches each hostel's reservations from Cloudbeds.
- Links each reservation's guest to our customer, matching the way the Cloudbeds guest import does.
- Shows the reservation's details, then any tours that guest has booked with us.
