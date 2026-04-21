# Game Design Document: Spardle

## 1. Executive Summary
**Spardle** is a fast-paced, competitive multiplayer spin on the classic word-guessing formula. Unlike the solitary daily ritual, this version focuses on high-stakes lobby customization, rapid-fire logic, and head-to-head showdowns. It supports dynamic word lengths (1–64 characters) and provides robust host-controlled sequencing.

---

## 2. Gameplay Mechanics

### 2.1 The Core Loop
Players have a limited number of attempts to guess a hidden word. Feedback is provided via color-coded tiles:
* **Correct:** Letter is in the word and in the correct spot.
* **Present:** Letter is in the word but in the wrong spot.
* **Absent:** Letter is not in the word.

### 2.2 Hard Mode
If enabled by the host, any revealed hints (Correct or Present letters) **must** be used in all subsequent guesses. The UI will reject "illegal" submissions that do not follow this rule.

### 2.3 Dynamic Guess Limit (G)
To maintain balance across different word lengths, the maximum number of guesses scales based on the word length ($L$) using the following algorithm:
$$G = \text{Round}(6 + k \cdot \ln(L / 5))$$
* **L:** Length of the target word (1 to 64).
* **k:** Difficulty scaling constant (Host configurable: Gentle, Standard (2), Brutal).
* *Note: A standard 5-letter word results in 6 guesses.*

### 2.4 Color Blink Accessibility Option
A client-side toggle for color vision deficiency:
* **Standard:** Green (Correct), Yellow (Present).
* **High-Contrast:** High-contrast Blue (Correct), High-contrast Red (Present).
* This setting is individual to the player/host and does not affect other players in the lobby.

---

## 3. Host Customization & Lobby Rules

### 3.1 Word Pool Selection
Hosts define the source of the hidden words:
1.  **NYT Standard:** Curated 5-letter list.
2.  **Full Dictionary:** Massive list filtered by chosen word length.
3.  **Host Defined:** A single word or a custom list of words (1–64 chars).
4.  **CSV Upload:** A file containing a custom list of words.

### 3.2 Round Limits & Defaults
The host sets the number of rounds, subject to the pool size:
* **The Pool Cap:** Rounds are capped at the total number of words provided (except in *Random with Repeats*).
* **Smart Defaults:**
    * If pool size $\le 12$: Default is **Full Pool Size**.
    * If pool size $> 12$: Default is **3 Rounds**.

### 3.3 Word Ordering Settings
* **Random (No Repeats):** Shuffles the pool; words are used once.
* **Random (With Repeats):** Any word can appear in any round; no round cap.
* **List Order:** Words appear in the exact order entered/uploaded.
* **Reverse List Order:** Words appear from the bottom of the list to the top.

### 3.4 Round Timer
To prevent games from stalling due to inactive or slow players, a hard timer dictates the maximum length of a round.
* **Default:** 3 minutes per round.
* **Customization:** Hosts can configure the timer from 30 seconds up to 10 minutes, or disable it entirely (Unlimited).
* If the timer hits 0:00, the round ends immediately. Any player who has not submitted the correct word is marked as a DNF (Did Not Finish).

### 3.5 Out-of-Pool Guessing (Dictionary Fallback)
* **Toggle (Default: ON):** Allows players to submit any valid dictionary word as a guess, even if the host is using a highly restricted custom word pool or CSV upload. 
* *Design Note:* This ensures players can still use strategic "burner words" to eliminate letters without being constrained by the host's specific theme. If toggled OFF, players can *only* guess words that exist within the host's custom pool.

### 3.6 Compound Word Guesses (Concatenation)
To ensure players can still use strategic "burner" guesses on longer target words, hosts can allow players to submit a string of multiple valid words combined into one (e.g., guessing "busywaiting" for an 11-letter target, combining "busy" and "waiting").
* **Dynamic Defaults:** * **ON** by default if the target word is $> 6$ characters.
    * **OFF** by default if the target word is $\le 6$ characters.
* **Customization:** Hosts can manually force this setting ON or OFF regardless of word length.

---

## 4. Win Conditions

Hosts select how a winner is determined for each round:
* **Mode A: The Sprinter (First to Solve):** The first player to submit the correct word wins.
* **Mode B: The Tactician (Fewest Guesses):** The player with the fewest attempts wins.
    * **Tie-breaker:** Faster completion time wins.
* **Wait for All (Toggle):** If ON, the round continues until everyone solves the word **or the Round Timer expires**. This ensures the game maintains momentum.
* **Reveal Answer (Toggle):** Shows the solution to all players at the end of the round.

---

## 5. UI & Technical Specifications

### 5.1 Adaptive Game Board
* **Scaling Grid:** Tiles automatically resize to fit the screen based on word length.
* **The Legacy View Bar:** A plain-text box below the guess strip for long words. It shows correctly guessed letters (e.g., `A _ _ L _`) to help players track progress.
* **Dynamic Keyboard:** Tracks letter status (Correct, Present, Absent) using the user's selected color palette.

### 5.2 Technical Requirements
* **Timestamp Precision:** Server-side timestamps (milliseconds) are used to resolve ties.
* **Validation:** Rejects host inputs or CSV entries exceeding 64 characters.
* **Compound Word Validation:** Validating compound words requires a dynamic programming or recursive word break algorithm in the C# or JavaScript backend to verify if the submitted string can be perfectly partitioned into valid dictionary entries before accepting the guess.

---

## 6. Scoring System
Final ranking is determined by cumulative points across all rounds.

* **1st Place:** 10 pts
* **2nd Place:** 5 pts
* **3rd Place:** 2 pts
* **Solve (but not top 3):** 1 pt
* **DNF (Did Not Finish):** 0 pts *(Applies if a player exhausts all guesses or the Round Timer expires before they find the solution).*

### Tie-Breaking Rules:
* **Absolute Ties:** If two or more players achieve the exact same winning metric (e.g., identical guess count and server-side millisecond timestamp), they receive the full points for that position. 
    * *Example:* If two players tie for 2nd place, both receive 5 points. The next fastest/best player would be awarded 3rd place (2 points).