package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import com.rextechnologies.flint.castcore.capability.CastMode
import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.media.MediaItem
import com.rextechnologies.flint.castcore.media.MediaState
import com.rextechnologies.flint.castcore.screen.PlaybackClock
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.EmptyState
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.Pill
import com.rextechnologies.flint.design.ProgressTrack
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.SignalButton
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState

/**
 * The Media tab: the remote for a file the television is playing.
 *
 * One card per state, and the state is the television's own report rather than a phone-side clock:
 * the scrubber sits where the receiver says it is. The picker is the system's, never a browser of
 * this app's own, and nothing on this screen shows or sends a path.
 */
@Composable
fun MediaScreen(state: MobileUiState, controller: MobileController, onPickVideo: () -> Unit) {
    val tab = MobileTab.MEDIA
    val media = state.media
    PageHeading(
        eyebrow = tab.eyebrow,
        headline = tab.title,
        statusText = stateWord(media),
        statusTone = stateTone(media),
    )

    Spacer(Modifier.height(FlintSpace.Small))
    val report = state.report
    if (report == null) {
        // One state, not two. Rendering the empty state under a verdict card said both "nothing is
        // known about this pair" and "here is what this pair can do" on the same screen.
        EmptyState(
            glyph = MediaCopy.empty.glyph,
            title = MediaCopy.empty.title,
            body = MediaCopy.empty.body,
        )
        return
    }

    val verdict = report[CastMode.MEDIA_HANDOFF]
    if (media is MediaState.Idle) {
        SectionLabel("What this pair can do")
        ModeCard(verdict)
        Spacer(Modifier.height(FlintSpace.Small))
    }

    SectionLabel(MediaCopy.SECTION_NOW)
    if (!state.isConnected) {
        InfoCard(borderTone = Tone.Line) {
            FlintText(text = "Not connected", style = FlintType.TitleMedium)
            FlintText(text = MediaCopy.NOT_CONNECTED, style = FlintType.BodyMedium.copy(color = FlintColors.Muted))
        }
        return
    }

    when (media) {
        MediaState.Idle -> InfoCard(borderTone = if (verdict.isOfferable) Tone.Signal else Tone.Line) {
            AdvisoryBlock(heading = "WHY IT IS SENT RATHER THAN FETCHED", body = MediaCopy.WHY_PUSHED)
            SignalButton(text = MediaCopy.PICK_ACTION, onClick = onPickVideo, enabled = verdict.isOfferable)
        }

        is MediaState.Preparing -> ItemCard(media.item, MediaCopy.PREPARING_WORD, Tone.Neutral) {
            OutlineAction(text = MediaCopy.CANCEL_ACTION, onClick = controller::cancelMediaTransfer)
        }

        is MediaState.Sending -> ItemCard(media.item, MediaCopy.SENDING_WORD, Tone.Signal) {
            val fraction = media.fraction
            FlintText(
                text = fraction?.let { MediaCopy.sendingFraction(it) } ?: MediaCopy.sendingBytes(media.bytesSent),
                style = FlintType.BodySmall.copy(color = FlintColors.Muted),
                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
            )
            ProgressTrack(fraction = fraction?.toFloat(), contentDescription = MediaCopy.SENDING_WORD)
            OutlineAction(text = MediaCopy.CANCEL_ACTION, onClick = controller::cancelMediaTransfer)
        }

        is MediaState.Buffering -> ItemCard(media.item, MediaCopy.BUFFERING_WORD, Tone.Signal) {
            OutlineAction(text = MediaCopy.STOP_ACTION, onClick = controller::stopMedia, tone = Tone.Live)
        }

        is MediaState.Playing -> Transport(
            item = media.item,
            word = MediaCopy.PLAYING_WORD,
            positionMs = media.positionMs,
            durationMs = media.durationMs,
            playing = true,
            controller = controller,
        )

        is MediaState.Paused -> Transport(
            item = media.item,
            word = MediaCopy.PAUSED_WORD,
            positionMs = media.positionMs,
            durationMs = media.durationMs,
            playing = false,
            controller = controller,
        )

        is MediaState.Ended -> ItemCard(media.item, MediaCopy.ENDED_WORD, Tone.Neutral) {
            Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                SignalButton(text = MediaCopy.PICK_ANOTHER_ACTION, onClick = onPickVideo)
                OutlineAction(text = MediaCopy.CLEAR_ACTION, onClick = controller::clearMedia)
            }
        }

        is MediaState.Failed -> InfoCard(borderTone = Tone.Live) {
            Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                FlintText(
                    text = media.item?.title ?: MediaCopy.FAILED_WORD,
                    style = FlintType.TitleMedium,
                    modifier = Modifier.weight(1f),
                )
                Pill(text = MediaCopy.FAILED_WORD, tone = Tone.Live)
            }
            // The receiver's own words where it gave any. A decoder's error code is not for a
            // person, and the television has already turned it into a sentence.
            FlintText(
                text = media.message,
                style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
            )
            Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                SignalButton(text = MediaCopy.PICK_ANOTHER_ACTION, onClick = onPickVideo)
                OutlineAction(text = MediaCopy.CLEAR_ACTION, onClick = controller::clearMedia)
            }
        }
    }
}

/** The title, the size when the provider gave one, and a word for where things are. */
@Composable
private fun ItemCard(
    item: MediaItem,
    word: String,
    tone: Tone,
    content: @Composable () -> Unit,
) {
    InfoCard(borderTone = tone) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            FlintText(text = item.title, style = FlintType.TitleMedium, modifier = Modifier.weight(1f))
            Pill(text = word, tone = tone)
        }
        MediaCopy.sizeLabel(item.sizeBytes)?.let {
            FlintText(text = it, style = FlintType.BodySmall.copy(color = FlintColors.Muted))
        }
        content()
    }
}

/**
 * The remote.
 *
 * The clock and the track follow the receiver's reported position. A tap on the track seeks; a
 * duration the receiver has not reported yet leaves the track empty rather than guessed.
 */
@Composable
private fun Transport(
    item: MediaItem,
    word: String,
    positionMs: Long,
    durationMs: Long,
    playing: Boolean,
    controller: MobileController,
) {
    ItemCard(item, word, Tone.Signal) {
        val fraction = if (durationMs > 0) (positionMs.toDouble() / durationMs).coerceIn(0.0, 1.0).toFloat() else null
        val clock = if (durationMs > 0) {
            "${PlaybackClock.format(positionMs)} / ${PlaybackClock.format(durationMs)}"
        } else {
            PlaybackClock.format(positionMs)
        }
        FlintText(text = clock, style = FlintType.Readout)
        ProgressTrack(
            fraction = fraction,
            onSeek = if (durationMs > 0) {
                { where -> controller.seekMedia((where * durationMs).toLong()) }
            } else {
                null
            },
            contentDescription = clock,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            if (playing) {
                SignalButton(text = MediaCopy.PAUSE_ACTION, onClick = controller::pauseMedia)
            } else {
                SignalButton(text = MediaCopy.PLAY_ACTION, onClick = controller::playMedia)
            }
            OutlineAction(text = MediaCopy.STOP_ACTION, onClick = controller::stopMedia, tone = Tone.Live)
        }
        OutlineAction(text = MediaCopy.CLEAR_ACTION, onClick = controller::clearMedia)
    }
}

private fun stateWord(media: MediaState): String = when (media) {
    MediaState.Idle -> "Idle"
    is MediaState.Preparing -> MediaCopy.PREPARING_WORD
    is MediaState.Sending -> MediaCopy.SENDING_WORD
    is MediaState.Buffering -> MediaCopy.BUFFERING_WORD
    is MediaState.Playing -> MediaCopy.PLAYING_WORD
    is MediaState.Paused -> MediaCopy.PAUSED_WORD
    is MediaState.Ended -> MediaCopy.ENDED_WORD
    is MediaState.Failed -> MediaCopy.FAILED_WORD
}

private fun stateTone(media: MediaState): Tone = when (media) {
    MediaState.Idle, is MediaState.Ended, is MediaState.Preparing -> Tone.Neutral
    is MediaState.Sending, is MediaState.Buffering, is MediaState.Playing, is MediaState.Paused -> Tone.Signal
    is MediaState.Failed -> Tone.Live
}
