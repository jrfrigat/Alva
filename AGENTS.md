<!-- aiko:begin -->
When Aiko MCP is available, use it as the durable project workflow and task memory. Any work the user
asks for starts with a card: create it in Aiko first (aiko_create_card, or /aiko-create), and stop
there - the card is the answer to the request. An order is still a request: "поправь ...", "исправь ..."
and "нужно ..." earn a card and nothing more, and the work begins only when the user asks for it
(aiko-run <cardId>, or "выполни <cardId>"). A card in its backlog stage has no work in it yet, so never
create or change files for a
card before you have started the stage you are working in with aiko_start_stage - the start moves the
card into that stage and is what records the work. A card may wait for another one: aiko_get_card lists
the cards that block it, and aiko_start_stage refuses a blocked card, naming the blocker. When that
happens, do not work this card - say so to the user and offer the blocking card instead. Do what the
stage's instruction asks for, produce
its required artifacts and complete it with aiko_complete_stage; only then does the card move on, and
aiko_move_card refuses to advance a card whose stage is not finished. Before you complete a stage,
re-estimate the card with aiko_estimate_card: the readiness criterion is what says the work is done,
and it must describe the card as it is after your change - a stage is not completed with a score
nobody refreshed. The card's feed is the notebook between stages: read it with aiko_list_comments before
you work a stage, and post the outcome with aiko_add_comment - signed with your adapter id - before you
complete it, so what one stage learned is not lost on the next. One run is one stage: work the next
stage only when the user asks again, and
/aiko-run <cardId> --all is the explicit exception - it
walks the pipeline and descends into the card's children, creating the ones a container's stage calls
for and working each child before going back to its parent, and even then it stops when a stage asks the
user a question, when an agent fails or hits its limit, when a stage forbids an action, or when a
required artifact cannot be produced. Read the
project context before touching files. Warn before modifying files outside the card scopeFiles and
record the actual changed files. A project can be linked to other projects: aiko_list_links lists them
with what each one is for, where its reference lives and when work belongs there. Before you reach for
something a neighbour owns - a library, a component, a pattern - read the reference its entry names and
search this project's memory first, rather than inferring it from a package installed here: the
reference is recorded so that no agent has to guess where the neighbour's documentation lives. When a
request belongs to a linked project, file it there with
aiko_create_card_in_project and pass originProjectId and originCardId so the receiving card remembers
where it came from - and read that project's context first, because its card types, its stages and its
rules are its own.
<!-- aiko:end -->
