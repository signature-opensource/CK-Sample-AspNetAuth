import { Component, signal } from '@angular/core';
import axios from 'axios';
import { HttpCrisEndpoint } from '@local/ck-gen/CK/Cris/HttpCrisEndpoint';
import { FrontInfoQCommand } from '@local/ck-gen/CK/Sample/Front/FrontInfoQCommand';

/**
 * A front without authentication: its "Update" button sends the FrontInfoQCommand to the host ("/.cris", on the same
 * origin) and displays the answer.
 */
@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.less'
})
export class App {
  readonly #endpoint = new HttpCrisEndpoint( axios.create() );
  protected readonly info = signal<string>( 'Click "Update" to ask the Front host.' );
  protected readonly busy = signal( false );

  protected async update(): Promise<void> {
    this.busy.set( true );
    try {
      this.info.set( await this.#endpoint.sendOrThrowAsync( new FrontInfoQCommand() ) ?? '' );
    } catch( e ) {
      this.info.set( `Error: ${e}` );
    } finally {
      this.busy.set( false );
    }
  }
}
