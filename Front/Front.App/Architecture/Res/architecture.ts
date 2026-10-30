import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { CrisError, HttpCrisEndpoint } from '@local/ck-gen';
import { NgAuthService } from '@local/ck-gen/CK/Ng/Cris/AspNet/Authentication/NgAuthService';
import { WhoAmIQCommand } from '@local/ck-gen/CK/Sample/WhoAmIQCommand';
import { ExecutionIdentity } from '@local/ck-gen/CK/Sample/ExecutionIdentity';
import { AskLaterQCommand } from '@local/ck-gen/CK/Sample/Front/AskLaterQCommand';
import { GetTopologyQCommand } from '@local/ck-gen/CK/Sample/Front/GetTopologyQCommand';
import { Topology } from '@local/ck-gen/CK/Sample/Front/Topology';

/** An answer of a party: who executed, for whom, through which caller party. */
interface Answer { title: string; identity?: ExecutionIdentity; error?: string; }

/** A predefined raw request of the console. */
interface RawRequest { label: string; path: string; body: string; withToken: boolean; contentType?: string; expected: string; }

@Component( {
  selector: 'sample-architecture',
  imports: [FormsModule, NzAlertModule, NzButtonModule, NzCardModule, NzCheckboxModule, NzInputModule, NzTagModule],
  templateUrl: './architecture.html'
} )
export class Architecture implements OnInit {
  readonly #auth = inject( NgAuthService );
  readonly #cris = inject( HttpCrisEndpoint );

  protected readonly topology = signal<Topology | undefined>( undefined );
  protected readonly topologyError = signal( '' );
  protected readonly answers = signal<Answer[]>( [] );
  // The parties that can be asked: the front itself and its remotes.
  protected readonly parties = computed( () => {
    const t = this.topology();
    return t ? [t.localPartyFullName, ...t.remotes.map( r => r.fullName )] : [];
  } );
  protected readonly authCenter = computed( () => this.topology()?.remotes.find( r => r.isAuthCenter )?.fullName );

  // The little request console.
  protected readonly requests: RawRequest[] = [
    {
      label: 'An [Internal] operation on /.cris/net',
      path: '/.cris/net',
      body: '["CK.IO.Authentication.IOpenSessionOperationCommand",{"scheme":"Basic","nextRefreshSecretHash":"x","nextBindingSecretHash":"y"}]',
      withToken: false,
      expected: 'Refused before its validation: only the public commands are received from HTTP (Cris.AspNet.ExposureRefused).'
    },
    {
      label: 'The same operation on /.cris',
      path: '/.cris',
      body: '["CK.IO.Authentication.IOpenSessionOperationCommand",{"scheme":"Basic"}]',
      withToken: false,
      expected: 'Not even readable: an internal command is never a TypeScript type (the "TypeScript" type set).'
    },
    {
      label: 'A login on /.cris',
      path: '/.cris',
      body: '["CK.IO.Authentication.Weak.IWeakLoginCommand",{"weakActorName":"Operator"}]',
      withToken: false,
      expected: 'A session command must be sent to /.cris/auth (the route of the session cookies).'
    },
    {
      label: 'The remote login callback sent by a client',
      path: '/.cris/auth',
      body: '["CK.IO.Authentication.IRemoteLoginCallbackCommand",{"state":"s","code":"c"}]',
      withToken: false,
      expected: 'Refused: this command is built by the callback endpoint only.'
    },
    {
      label: 'Hello without the token',
      path: '/.cris',
      body: '["CK.Sample.Front.IHelloCommand",{"actorId":0}]',
      withToken: false,
      expected: 'The anonymous: the Normal level is required.'
    },
    {
      label: 'Who am I at the AuthCenter, for another actor',
      path: '/.cris',
      body: '["CK.Sample.IWhoAmIQCommand",{"actorId":1,"targetPartyFullName":"Sample/$Backend/#Dev"}]',
      withToken: true,
      expected: 'Refused by the front: the actor of the command must be the authenticated one (its incoming validation).'
    },
    {
      label: 'A party command for an unknown party',
      path: '/.cris',
      body: '["CK.Sample.IWhoAmIQCommand",{"actorId":0,"targetPartyFullName":"Nowhere/$Nobody/#Dev"}]',
      withToken: true,
      expected: 'No route (set the actorId to yours): the front knows no such party.'
    },
    {
      label: 'A plain text POST',
      path: '/.cris',
      body: 'hello',
      withToken: false,
      contentType: 'text/plain',
      expected: '415: a simple request that a browser could send cross-site is refused.'
    },
    {
      label: 'The former /.cris/auth-center route',
      path: '/.cris/auth-center',
      body: '["CK.IO.User.UserPasskey.IGetPasskeysQCommand",{"actorId":0}]',
      withToken: true,
      expected: 'Gone: the commands handled by the AuthCenter are posted to /.cris and dispatched.'
    }
  ];
  protected readonly selected = signal<RawRequest | undefined>( undefined );
  protected path = '/.cris';
  protected body = '';
  protected withToken = true;
  protected contentType = 'application/json';
  protected readonly response = signal( '' );

  async ngOnInit(): Promise<void> {
    await this.refreshTopology();
  }

  async refreshTopology(): Promise<void> {
    this.topologyError.set( '' );
    const r = ( await this.#cris.sendAsync( new GetTopologyQCommand() ) ).result;
    if( r instanceof CrisError ) this.topologyError.set( r.errors.join( ' ' ) );
    else this.topology.set( r );
  }

  // "Here and there": the same party command, executed where its target is.
  async whoAmI( target: string ): Promise<void> {
    const c = new WhoAmIQCommand();
    c.targetPartyFullName = target;
    this.#addAnswer( `Who am I at ${target}?`, ( await this.#cris.sendAsync( c ) ).result );
  }

  // "Later": the front asks in its background (no user's proof: it only vouches for the user).
  async askLater( target: string ): Promise<void> {
    const c = new AskLaterQCommand();
    c.targetPartyFullName = target;
    this.#addAnswer( `Who am I at ${target}, asked later by the front?`, ( await this.#cris.sendAsync( c ) ).result );
  }

  #addAnswer( title: string, result: ExecutionIdentity | CrisError | undefined ): void {
    const a: Answer = result instanceof CrisError
                        ? { title, error: result.errors.join( ' ' ) }
                        : { title, identity: result };
    this.answers.update( list => [a, ...list] );
  }

  protected partyName( partyActorId: number ): string {
    const t = this.topology();
    if( !t || partyActorId === 0 ) return '#0 (none)';
    if( t.localPartyActorId === partyActorId ) return `#${partyActorId} (${t.localPartyFullName})`;
    const r = t.remotes.find( p => p.partyActorId === partyActorId );
    return r ? `#${partyActorId} (${r.fullName})` : `#${partyActorId}`;
  }

  select( r: RawRequest ): void {
    this.selected.set( r );
    this.path = r.path;
    this.body = r.body;
    this.withToken = r.withToken;
    this.contentType = r.contentType ?? 'application/json';
    this.response.set( '' );
  }

  // Sends the raw request with fetch (same origin: the cookies go with it) and dumps the answer.
  async send(): Promise<void> {
    const headers: Record<string, string> = { 'Content-Type': this.contentType };
    const token = this.#auth.authService.token;
    if( this.withToken && token ) headers['Authorization'] = `Bearer ${token}`;
    try {
      const r = await fetch( this.path, { method: 'POST', headers, body: this.body } );
      const text = await r.text();
      let pretty = text;
      try { pretty = JSON.stringify( JSON.parse( text ), null, 2 ); } catch { /* Not JSON. */ }
      this.response.set( `HTTP ${r.status} ${r.statusText}\n\n${pretty}` );
    } catch( e ) {
      this.response.set( `Failed: ${e}` );
    }
  }
}
