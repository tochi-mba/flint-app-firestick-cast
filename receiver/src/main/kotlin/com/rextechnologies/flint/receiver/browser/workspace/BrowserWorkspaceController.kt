package com.rextechnologies.flint.receiver.browser.workspace

/** Stateful convenience wrapper for one receiver browser surface. */
class BrowserWorkspaceController(
    private val reducer: BrowserWorkspaceReducer = BrowserWorkspaceReducer(),
    initialState: BrowserWorkspaceState = BrowserWorkspaceState(),
) {
    val capacity: BrowserWorkspaceCapacity get() = reducer.capacity

    @Volatile
    var state: BrowserWorkspaceState = initialState
        private set

    @Synchronized
    fun dispatch(action: BrowserWorkspaceAction): BrowserWorkspaceTransition {
        val transition = reducer.reduce(state, action)
        state = transition.state
        return transition
    }
}
