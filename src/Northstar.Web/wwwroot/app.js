// Tiny browser helper used by the Example Scenarios page (scenario 6): calls the same REST API the chat uses,
// so you can see that UI and API return identical numbers.
window.northstar = {
    postQuery: async function (question) {
        const response = await fetch('/api/query', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ question })
        });
        const text = await response.text();
        try {
            return JSON.stringify(JSON.parse(text), null, 2);
        } catch {
            return `HTTP ${response.status}\n${text}`;
        }
    }
};
