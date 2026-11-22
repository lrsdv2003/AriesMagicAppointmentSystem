(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const schedule = byId('dashboardSchedule');
  if (!schedule) return;

  const venue = byId('dashboardVenue');
  const date = byId('dashboardDate');
  const time = byId('dashboardTime');
  const pack = byId('dashboardPackage');
  const address = byId('dashboardAddress');
  const plan = byId('dashboardPlan');
  const planUrl = plan.href;
  const scheduleResult = byId('dashboardScheduleResult');
  const venueResult = byId('dashboardVenueResult');
  const alternatives = byId('dashboardAlternatives');
  const choices = byId('dashboardAlternativeChoices');
  const matches = byId('dashboardVenueMatches');
  const travel = byId('dashboardTravel');
  const money = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' });
  let scheduleVersion = 0;
  let venueVersion = 0;
  let verifiedSchedule = null;
  let verifiedVenue = null;
  let selectedVenue = null;

  function updatePlan() {
    const url = new URL(planUrl);
    if (verifiedSchedule) {
      for (const [key, value] of verifiedSchedule) if (value && key !== 'suggest') url.searchParams.set(key, value);
    }
    if (verifiedVenue) url.hash = new URLSearchParams({ venue: verifiedVenue }).toString();
    plan.href = url.href;
  }

  function show(panel, state, title, description) {
    const heading = document.createElement('strong');
    heading.textContent = title;
    panel.replaceChildren(heading);
    if (description) {
      const copy = document.createElement('p');
      copy.textContent = description;
      panel.append(copy);
    }
    panel.dataset.state = state;
    panel.hidden = false;
  }

  async function fetchJson(url, options) {
    const response = await fetch(url, { ...options, signal: AbortSignal.timeout(18000) });
    if (!response.ok && ![400, 422].includes(response.status)) throw new Error('Request failed');
    return response.json();
  }

  function resetVenue() {
    venueVersion++;
    venue.querySelector('button[type=submit]').disabled = false;
    venueResult.hidden = travel.hidden = true;
    matches.replaceChildren();
    verifiedVenue = null;
    updatePlan();
  }

  function resetSchedule() {
    scheduleVersion++;
    schedule.querySelector('button[type=submit]').disabled = false;
    scheduleResult.hidden = alternatives.hidden = true;
    verifiedSchedule = null;
    updatePlan();
  }

  date.addEventListener('input', () => { resetSchedule(); resetVenue(); });
  time.addEventListener('input', () => { resetSchedule(); resetVenue(); });
  pack.addEventListener('change', resetSchedule);
  address.addEventListener('input', () => { selectedVenue = null; resetVenue(); });

  schedule.addEventListener('submit', async event => {
    event.preventDefault();
    resetSchedule();
    const version = scheduleVersion;
    const button = event.submitter;
    const params = new URLSearchParams(new FormData(schedule));
    button.disabled = true;
    show(scheduleResult, 'loading', 'Checking availability…');
    try {
      const data = await fetchJson(schedule.action + '?' + params);
      if (version !== scheduleVersion) return;
      if (data.status === 'available') {
        verifiedSchedule = params;
        updatePlan();
        show(scheduleResult, 'available', data.message,
          pack.value ? null : 'At least one package fits this schedule.');
      } else if (data.status === 'unavailable') {
        show(scheduleResult, 'unavailable', data.message,
          data.alternatives.length ? null : 'No alternatives were found near this schedule. Try another date or package.');
        choices.replaceChildren();
        for (const slot of data.alternatives) {
          const option = document.createElement('button');
          option.type = 'button';
          option.className = 'cn-button';
          option.textContent = slot.label;
          option.addEventListener('click', () => {
            date.value = slot.date;
            time.value = slot.time;
            resetVenue();
            schedule.requestSubmit(button);
            button.focus();
          });
          choices.append(option);
        }
        alternatives.hidden = !data.alternatives.length;
      } else {
        show(scheduleResult, 'review', 'Invalid schedule', data.message);
      }
    } catch {
      if (version === scheduleVersion)
        show(scheduleResult, 'review', 'Unable to check availability', 'Please try again.');
    } finally {
      if (version === scheduleVersion) button.disabled = false;
    }
  });

  venue.addEventListener('submit', async event => {
    event.preventDefault();
    if (!date.reportValidity() || !time.reportValidity()) return;
    resetVenue();
    const version = venueVersion;
    const button = event.submitter;
    button.disabled = true;
    show(venueResult, 'loading', selectedVenue ? 'Checking venue…' : 'Searching for venues…');
    try {
      if (!selectedVenue) {
        const results = await fetchJson(venue.dataset.searchUrl + '?' + new URLSearchParams({ query: address.value.trim() }));
        if (version !== venueVersion) return;
        if (!results.length) {
          show(venueResult, 'review', 'Unable to verify venue', 'No matching venue was found. Try a more specific address or use manual entry in the booking form.');
          return;
        }
        show(venueResult, 'loading', 'Select a matching venue');
        for (const result of results) {
          const option = document.createElement('button');
          option.type = 'button';
          option.className = 'cn-button';
          option.textContent = result.displayName + (result.isPrecise ? '' : ' (approximate)');
          option.addEventListener('click', () => {
            selectedVenue = result;
            // The search query has a shorter limit than a resolved address.
            address.value = result.displayName;
            venue.requestSubmit(button);
            address.focus();
          });
          matches.append(option);
        }
        return;
      }

      const body = new FormData(venue);
      for (const [key, value] of Object.entries({
        eventDate: date.value, startTime: time.value, venueSelectionMethod: 'Search',
        partyVenue: selectedVenue.displayName, venueLatitude: selectedVenue.latitude, venueLongitude: selectedVenue.longitude
      })) body.set(key, value);

      const data = await fetchJson(venue.action, { method: 'POST', body });
      if (version !== venueVersion) return;
      if (!data.success) {
        show(venueResult, 'review', 'Unable to verify venue', data.message);
        return;
      }

      const status = data.outsideCoverage ? ['unavailable', 'Outside Service Area']
        : data.travelTimeInsufficient && data.distanceKm == null ? ['review', 'Unable to Verify']
        : data.travelTimeInsufficient ? ['unavailable', 'Travel Unavailable at This Time']
        : data.requiresManualReview ? ['review', 'Requires Staff Review']
        : data.isServiceable ? ['available', 'Serviceable'] : ['review', 'Unable to Verify'];
      show(venueResult, ...status, data.serviceabilityReason);
      if (data.distanceKm != null) {
        byId('dashboardDistance').textContent = data.distanceKm + ' km';
        byId('dashboardFee').textContent = data.travelFee == null ? 'Unavailable'
          : money.format(data.travelFee) + (data.requiresManualReview ? ' (provisional)' : '');
        travel.hidden = false;
      }
      if (data.isServiceable && data.distanceKm != null) {
        verifiedVenue = selectedVenue.displayName;
        updatePlan();
      }
    } catch {
      if (version === venueVersion)
        show(venueResult, 'review', 'Unable to verify venue', 'Please try again or enter the address manually in the booking form.');
    } finally {
      if (version === venueVersion) button.disabled = false;
    }
  });
})();
