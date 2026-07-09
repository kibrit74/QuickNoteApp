// QuickNoteApp Interactive Logic

document.addEventListener('DOMContentLoaded', () => {
    // ----------------------------------------------------
    // State management for mock database
    // ----------------------------------------------------
    let notes = [
        { id: 1, text: "DatabaseService SQLite ayarlarını kontrol et.", checked: true },
        { id: 2, text: "UI renk paletini güncelle (kırmızı, bej, füme, siyah).", checked: false }
    ];

    // ----------------------------------------------------
    // DOM Elements
    // ----------------------------------------------------
    // Simulator Triggers
    const btnTriggerNote = document.getElementById('btn-trigger-note-sim');
    const btnTriggerReview = document.getElementById('btn-trigger-review-sim');
    
    // Windows Popups
    const notePopup = document.getElementById('sim-note-popup');
    const reviewPopup = document.getElementById('sim-review-popup');
    const closeNoteBtn = document.getElementById('close-note-popup');
    const closeReviewBtn = document.getElementById('close-review-popup');
    
    // Note Input & Stats
    const noteInput = document.getElementById('sim-note-input');
    const charCounter = document.getElementById('sim-char-counter');
    const notesCountBadge = document.getElementById('notes-count');
    const notesUl = document.getElementById('sim-notes-ul');
    
    // Toast Alert
    const toast = document.getElementById('sim-toast-alert');
    const toastText = document.getElementById('toast-note-text');

    // Review Tabs
    const tabNotes = document.getElementById('tab-notes');
    const tabNotifications = document.getElementById('tab-notifications');
    const contentNotes = document.getElementById('content-notes');
    const contentNotifications = document.getElementById('content-notifications');

    // Gallery Tabs
    const galleryTabs = document.querySelectorAll('.gallery-tab-btn');
    const gallerySlides = document.querySelectorAll('.gallery-content-slide');

    // ----------------------------------------------------
    // Functions
    // ----------------------------------------------------
    
    // Open Quick Note Window
    function openNotePopup() {
        reviewPopup.classList.remove('active');
        notePopup.classList.add('active');
        noteInput.focus();
    }

    // Close Quick Note Window
    function closeNotePopup() {
        notePopup.classList.remove('active');
        noteInput.value = '';
        updateCharCount();
    }

    // Open Daily Review Window
    function openReviewPopup() {
        notePopup.classList.remove('active');
        reviewPopup.classList.add('active');
        renderNotes();
    }

    // Close Daily Review Window
    function closeReviewPopup() {
        reviewPopup.classList.remove('active');
    }

    // Update Char Count
    function updateCharCount() {
        const length = noteInput.value.length;
        charCounter.textContent = `${length} / 150`;
    }

    // Trigger Toast Notification
    function showToast(text) {
        toastText.textContent = text;
        toast.classList.add('active');
        
        setTimeout(() => {
            toast.classList.remove('active');
        }, 3000);
    }

    // Render Note List from local state
    function renderNotes() {
        notesUl.innerHTML = '';
        notesCountBadge.textContent = notes.length;
        
        notes.forEach((note) => {
            const li = document.createElement('li');
            li.innerHTML = `
                <label class="checkbox-container">
                    <input type="checkbox" ${note.checked ? 'checked' : ''} data-id="${note.id}">
                    <span class="checkmark"></span>
                    <span class="note-text">${escapeHtml(note.text)}</span>
                </label>
            `;
            
            // Listen to checkbox toggles
            const checkbox = li.querySelector('input');
            checkbox.addEventListener('change', (e) => {
                const noteId = parseInt(e.target.getAttribute('data-id'));
                const foundNote = notes.find(n => n.id === noteId);
                if (foundNote) {
                    foundNote.checked = e.target.checked;
                }
            });

            notesUl.appendChild(li);
        });
    }

    // Simple HTML escaping helper
    function escapeHtml(str) {
        return str
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    // Add Note to array
    function saveNote() {
        const text = noteInput.value.trim();
        if (text.length === 0) return;

        const newNote = {
            id: Date.now(),
            text: text,
            checked: false
        };

        notes.push(newNote);
        closeNotePopup();
        showToast(text.length > 30 ? `"${text.substring(0, 27)}..." başarıyla SQLite tabanına kaydedildi.` : `"${text}" başarıyla SQLite tabanına kaydedildi.`);
        renderNotes();
    }

    // ----------------------------------------------------
    // Event Listeners
    // ----------------------------------------------------

    // Click Triggers for popups
    if (btnTriggerNote) btnTriggerNote.addEventListener('click', openNotePopup);
    if (btnTriggerReview) btnTriggerReview.addEventListener('click', openReviewPopup);
    if (closeNoteBtn) closeNoteBtn.addEventListener('click', closeNotePopup);
    if (closeReviewBtn) closeReviewBtn.addEventListener('click', closeReviewPopup);

    // Typing inside Note Input
    if (noteInput) {
        noteInput.addEventListener('input', updateCharCount);
        noteInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                saveNote();
            }
        });
    }

    // Review Window Tab System
    if (tabNotes && tabNotifications) {
        tabNotes.addEventListener('click', () => {
            tabNotes.classList.add('active');
            tabNotifications.classList.remove('active');
            contentNotes.classList.add('active');
            contentNotifications.classList.remove('active');
        });

        tabNotifications.addEventListener('click', () => {
            tabNotifications.classList.add('active');
            tabNotes.classList.remove('active');
            contentNotifications.classList.add('active');
            contentNotes.classList.remove('active');
        });
    }

    // UI Gallery Tab System
    galleryTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const target = tab.getAttribute('data-target');
            
            galleryTabs.forEach(t => t.classList.remove('active'));
            gallerySlides.forEach(s => s.classList.remove('active'));
            
            tab.classList.add('active');
            const targetSlide = document.getElementById(target);
            if (targetSlide) {
                targetSlide.classList.add('active');
            }
        });
    });

    // ----------------------------------------------------
    // Global Keyboard Hotkey Simulators
    // ----------------------------------------------------
    document.addEventListener('keydown', (e) => {
        // Ctrl + Shift + N
        if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 'n') {
            e.preventDefault();
            // Scroll to simulator section to give visual context
            const simSection = document.getElementById('simulator');
            if (simSection) {
                simSection.scrollIntoView({ behavior: 'smooth' });
            }
            openNotePopup();
        }
        
        // Ctrl + Shift + R
        if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 'r') {
            e.preventDefault();
            // Scroll to simulator section to give visual context
            const simSection = document.getElementById('simulator');
            if (simSection) {
                simSection.scrollIntoView({ behavior: 'smooth' });
            }
            openReviewPopup();
        }

        // Close on Escape
        if (e.key === 'Escape') {
            closeNotePopup();
            closeReviewPopup();
        }
    });

    // Initialize list count on load
    renderNotes();
});
