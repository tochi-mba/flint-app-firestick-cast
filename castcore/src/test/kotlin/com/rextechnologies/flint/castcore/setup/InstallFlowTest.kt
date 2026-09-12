package com.rextechnologies.flint.castcore.setup

import kotlin.test.Test
import kotlin.test.assertEquals

class InstallFlowTest {
    private val stages = listOf(
        ReceiverInstallStage.Unknown,
        ReceiverInstallStage.NotInstalled,
        ReceiverInstallStage.AwaitingAuthorisation,
        ReceiverInstallStage.Authorised,
        ReceiverInstallStage.Installing,
        ReceiverInstallStage.Installed,
        ReceiverInstallStage.Failed("refused"),
        ReceiverInstallStage.Impossible,
    )

    private val events = listOf(
        InstallEvent.Identified(receiverInstalled = true),
        InstallEvent.Identified(receiverInstalled = false),
        InstallEvent.AuthorisationRequired,
        InstallEvent.NotAndroid,
        InstallEvent.InstallStarted,
        InstallEvent.InstallSucceeded,
        InstallEvent.InstallFailed("no space"),
        InstallEvent.RemoveSucceeded,
        InstallEvent.RemoveFailed("not installed"),
        InstallEvent.Unreachable("timed out"),
    )

    @Test
    fun `nothing leaves impossible`() {
        events.forEach { event ->
            assertEquals(ReceiverInstallStage.Impossible, InstallFlow.reduce(ReceiverInstallStage.Impossible, event))
        }
    }

    @Test
    fun `vega makes every other stage impossible`() {
        stages.forEach { stage ->
            assertEquals(ReceiverInstallStage.Impossible, InstallFlow.reduce(stage, InstallEvent.NotAndroid))
        }
    }

    @Test
    fun `identification says whether the receiver is there, from any stage`() {
        stages.filter { it != ReceiverInstallStage.Impossible }.forEach { stage ->
            assertEquals(ReceiverInstallStage.Installed, InstallFlow.reduce(stage, InstallEvent.Identified(true)))
            assertEquals(ReceiverInstallStage.NotInstalled, InstallFlow.reduce(stage, InstallEvent.Identified(false)))
        }
    }

    @Test
    fun `the television's prompt is waited for, and only the television ends the wait`() {
        val waiting = InstallFlow.reduce(ReceiverInstallStage.NotInstalled, InstallEvent.AuthorisationRequired)
        assertEquals(ReceiverInstallStage.AwaitingAuthorisation, waiting)
        // Nothing on the phone moves it forward: an unreachable television leaves it waiting.
        assertEquals(waiting, InstallFlow.reduce(waiting, InstallEvent.Unreachable("dropped")))
        // The television saying yes arrives as a later successful identification.
        assertEquals(ReceiverInstallStage.NotInstalled, InstallFlow.reduce(waiting, InstallEvent.Identified(false)))
    }

    @Test
    fun `an install runs from started to its outcome`() {
        val installing = InstallFlow.reduce(ReceiverInstallStage.NotInstalled, InstallEvent.InstallStarted)
        assertEquals(ReceiverInstallStage.Installing, installing)
        assertEquals(ReceiverInstallStage.Installed, InstallFlow.reduce(installing, InstallEvent.InstallSucceeded))
        assertEquals(
            ReceiverInstallStage.Failed("no space"),
            InstallFlow.reduce(installing, InstallEvent.InstallFailed("no space")),
        )
    }

    @Test
    fun `a link that drops mid-install is a failure, and at any other time is not the card's news`() {
        val dropped = InstallEvent.Unreachable("the link dropped")
        assertEquals(
            ReceiverInstallStage.Failed("the link dropped"),
            InstallFlow.reduce(ReceiverInstallStage.Installing, dropped),
        )
        stages.filter {
            it != ReceiverInstallStage.Installing && it != ReceiverInstallStage.Impossible
        }.forEach { stage ->
            assertEquals(stage, InstallFlow.reduce(stage, dropped), stage.toString())
        }
    }

    @Test
    fun `removal returns to not installed, or says why it could not`() {
        assertEquals(
            ReceiverInstallStage.NotInstalled,
            InstallFlow.reduce(ReceiverInstallStage.Installed, InstallEvent.RemoveSucceeded),
        )
        assertEquals(
            ReceiverInstallStage.Failed("not installed"),
            InstallFlow.reduce(ReceiverInstallStage.Installed, InstallEvent.RemoveFailed("not installed")),
        )
    }
}
